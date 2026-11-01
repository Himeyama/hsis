using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Xml.Linq;
using System.Buffers.Binary;

namespace Hsis.Core;

public static class ApplicationIcon
{
    private const int IconPixels = 256;
    public static string? FindExecutable(InstallPlan plan, PackageDefinition definition)
    {
        if (!string.IsNullOrWhiteSpace(definition.LaunchExecutable))
        {
            InstallFile? launch = plan.Files.FirstOrDefault(file => file.Destination.Equals(definition.LaunchExecutable, StringComparison.OrdinalIgnoreCase));
            if (launch is not null && Path.GetExtension(launch.Source).Equals(".exe", StringComparison.OrdinalIgnoreCase)) return launch.Source;
        }
        foreach (ScriptOperation operation in plan.Install)
        {
            string? target = operation.Command switch
            {
                "createshortcut" => operation.Arguments[1],
                "writeregstr" when operation.Arguments[2].Equals("DisplayIcon", StringComparison.OrdinalIgnoreCase) => operation.Arguments[3],
                _ => null
            };
            if (target is null || !target.StartsWith("$INSTDIR\\", StringComparison.OrdinalIgnoreCase)) continue;
            InstallFile? file = plan.Files.FirstOrDefault(file => file.Destination.Equals(target[9..], StringComparison.OrdinalIgnoreCase));
            if (file is not null && Path.GetExtension(file.Source).Equals(".exe", StringComparison.OrdinalIgnoreCase)) return file.Source;
        }
        List<InstallFile> executables = plan.Files.Where(file => Path.GetExtension(file.Source).Equals(".exe", StringComparison.OrdinalIgnoreCase)).ToList();
        return executables.FirstOrDefault(file => Path.GetFileNameWithoutExtension(file.Source).Equals(definition.Name, StringComparison.OrdinalIgnoreCase))?.Source
            ?? (executables.Count == 1 ? executables[0].Source : null);
    }

    public static bool TryWrite(InstallPlan plan, PackageDefinition definition, string output, Action<string> log, string? script = null)
    {
        string? executable = FindExecutable(plan, definition);
        if (executable is null || !OperatingSystem.IsWindows()) return false;
        try
        {
            if (Extract(executable, output, log)) return true;
            string? iconFile = FindIconFile(plan, executable, script);
            if (iconFile is not null) { SaveImage(iconFile, output); log($"アプリアイコン: {iconFile}"); return true; }
            log($"対象アプリのアイコンを取得できません: {executable}");
            return false;
        }
        catch (Exception ex) when (ex is ExternalException or ArgumentException or IOException or UnauthorizedAccessException)
        {
            log($"アプリアイコンを取得できませんでした: {ex.Message}");
            return false;
        }
    }

    public static string? FindIconFile(InstallPlan plan, string executable, string? script)
    {
        string name = Path.GetFileNameWithoutExtension(executable);
        InstallFile? packagedIcon = plan.Files.FirstOrDefault(file => Path.GetFileNameWithoutExtension(file.Source).Equals(name, StringComparison.OrdinalIgnoreCase)
            && Path.GetExtension(file.Source).ToLowerInvariant() is ".ico" or ".png");
        if (packagedIcon is not null) return packagedIcon.Source;
        if (script is null) return null;
        string? directory = Path.GetDirectoryName(Path.GetFullPath(script));
        // Inspect only the definition's nearby project, never unrelated projects elsewhere.
        for (int level = 0; level < 3 && directory is not null; level++, directory = Path.GetDirectoryName(directory))
        {
            string project = Path.Combine(directory, name + ".csproj");
            if (!File.Exists(project)) continue;
            string? relative = XDocument.Load(project).Descendants().FirstOrDefault(element => element.Name.LocalName == "ApplicationIcon" && !string.IsNullOrWhiteSpace(element.Value))?.Value.Trim();
            if (relative is null || relative.Contains("$(", StringComparison.Ordinal)) continue;
            string icon = Path.GetFullPath(Path.Combine(directory, relative));
            if (File.Exists(icon)) return icon;
        }
        return null;
    }

    [SupportedOSPlatform("windows")]
    private static void SaveImage(string input, string output)
    {
        if (Path.GetExtension(input).Equals(".ico", StringComparison.OrdinalIgnoreCase))
        {
            byte[] data = File.ReadAllBytes(input);
            if (data.Length < 6 || BinaryPrimitives.ReadUInt16LittleEndian(data.AsSpan(0, 2)) != 0 || BinaryPrimitives.ReadUInt16LittleEndian(data.AsSpan(2, 2)) != 1)
                throw new InvalidDataException("ICO のヘッダーが不正です。");
            int count = BinaryPrimitives.ReadUInt16LittleEndian(data.AsSpan(4, 2));
            if (count == 0 || data.Length < 6 + 16 * count) throw new InvalidDataException("ICO の画像一覧が不正です。");
            int selected = 6;
            int largest = 0;
            int bestDepth = 0;
            for (int index = 0; index < count; index++)
            {
                int entry = 6 + index * 16;
                int width = data[entry] == 0 ? 256 : data[entry];
                int height = data[entry + 1] == 0 ? 256 : data[entry + 1];
                int depth = BinaryPrimitives.ReadUInt16LittleEndian(data.AsSpan(entry + 6, 2));
                if (width * height > largest || width * height == largest && depth > bestDepth)
                { selected = entry; largest = width * height; bestDepth = depth; }
            }
            uint length = BinaryPrimitives.ReadUInt32LittleEndian(data.AsSpan(selected + 8, 4));
            uint offset = BinaryPrimitives.ReadUInt32LittleEndian(data.AsSpan(selected + 12, 4));
            if (length == 0 || (ulong)offset + length > (ulong)data.Length) throw new InvalidDataException("ICO の画像範囲が不正です。");
            ReadOnlySpan<byte> frame = data.AsSpan((int)offset, (int)length);
            ReadOnlySpan<byte> pngSignature = [137, 80, 78, 71, 13, 10, 26, 10];
            if (frame.StartsWith(pngSignature))
            {
                // Preserve the original high-resolution PNG. Icon(string, size)
                // can ignore PNG frames and pick a smaller bitmap instead.
                File.WriteAllBytes(output, frame.ToArray());
                return;
            }
            using MemoryStream singleFrame = new();
            using (BinaryWriter writer = new(singleFrame, System.Text.Encoding.UTF8, true))
            {
                writer.Write((ushort)0); writer.Write((ushort)1); writer.Write((ushort)1);
                writer.Write(data.AsSpan(selected, 12)); writer.Write(22U); writer.Write(frame);
            }
            singleFrame.Position = 0;
            using Icon icon = new(singleFrame);
            using Bitmap bitmap = icon.ToBitmap();
            bitmap.Save(output, ImageFormat.Png);
        }
        else
        {
            using Image image = Image.FromFile(input);
            image.Save(output, ImageFormat.Png);
        }
    }

    [SupportedOSPlatform("windows")]
    private static bool Extract(string executable, string output, Action<string> log)
    {
        int result = SHDefExtractIcon(executable, 0, 0, out nint largeIcon, out nint smallIcon, IconPixels);
        try
        {
            if (result != 0 || largeIcon == 0) return false;
            using Icon icon = Icon.FromHandle(largeIcon);
            using Bitmap bitmap = icon.ToBitmap();
            bitmap.Save(output, ImageFormat.Png);
            log($"アプリアイコン: {executable}");
            return true;
        }
        finally
        {
            if (largeIcon != 0) DestroyIcon(largeIcon);
            if (smallIcon != 0) DestroyIcon(smallIcon);
        }
    }

    [DllImport("shell32.dll", EntryPoint = "SHDefExtractIconW", CharSet = CharSet.Unicode)]
    private static extern int SHDefExtractIcon(string iconFile, int iconIndex, uint flags, out nint largeIcon, out nint smallIcon, uint iconSize);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DestroyIcon(nint icon);
}
