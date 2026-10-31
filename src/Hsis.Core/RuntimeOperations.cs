using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using Microsoft.Win32;

namespace Hsis.Core;

public static class RuntimeOperations
{
    public static void CheckRequirements(IEnumerable<ScriptOperation> operations)
    {
        Stack<bool> parents = new();
        bool active = true;
        string message = "この環境にはインストールできません。";
        foreach (ScriptOperation operation in operations)
        {
            switch (operation.Command)
            {
                case "${if}": case "${ifnot}":
                    bool condition = operation.Arguments[0] switch
                    {
                        "${RunningX64}" => RuntimeInformation.OSArchitecture == Architecture.X64,
                        "${AtLeastBuild}" => OperatingSystem.IsWindowsVersionAtLeast(10, 0, int.Parse(operation.Arguments[1])),
                        _ => throw new InvalidDataException("不明な実行時条件です。")
                    };
                    parents.Push(active); active = active && (operation.Command == "${if}" ? condition : !condition); break;
                case "${else}": if (parents.Count == 0) throw new InvalidDataException("対応する If がありません。"); active = parents.Peek() && !active; break;
                case "${endif}": if (parents.Count == 0) throw new InvalidDataException("対応する If がありません。"); active = parents.Pop(); break;
                case "messagebox": if (active) message = operation.Arguments[1]; break;
                case "abort": if (active) throw new InvalidOperationException(message); break;
                default: throw new InvalidDataException($"未対応の初期化命令: {operation.Command}");
            }
        }
        if (parents.Count != 0) throw new InvalidDataException("EndIf がありません。");
    }

    public static string ResolvePath(string value, string installDirectory)
    {
        if (value.StartsWith("$SMPROGRAMS\\", StringComparison.OrdinalIgnoreCase))
            return Confine(Environment.GetFolderPath(Environment.SpecialFolder.Programs), value[12..]);
        return Confine(installDirectory, DefinitionCompiler.NormalizeDestination(value));
    }

    private static string Confine(string root, string relative)
    {
        if (string.IsNullOrWhiteSpace(root)) throw new InvalidDataException("フォルダーを取得できません。");
        string fullRoot = Path.GetFullPath(root);
        string result = Path.GetFullPath(Path.Combine(fullRoot, relative));
        if (result != fullRoot && !result.StartsWith(Path.TrimEndingDirectorySeparator(fullRoot) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("操作先が指定フォルダーの外です。");
        InstallationEngine.RejectLinks(result);
        return result;
    }

    public static void Execute(IEnumerable<ScriptOperation> operations, string installDirectory, string? setupExecutable, IProgress<string>? progress)
    {
        foreach (ScriptOperation operation in operations)
        {
            string[] args = operation.Arguments;
            progress?.Report(operation.Command);
            switch (operation.Command)
            {
                case "createdirectory": Directory.CreateDirectory(ResolvePath(args[0], installDirectory)); break;
                case "writeuninstaller":
                    if (setupExecutable is null || !File.Exists(setupExecutable) || Path.GetFileName(setupExecutable).Equals("dotnet.exe", StringComparison.OrdinalIgnoreCase))
                        throw new InvalidOperationException("アンインストーラー生成には発行済みセットアップが必要です。");
                    string target = ResolvePath(args[0], installDirectory);
                    Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                    File.Copy(setupExecutable, target, true); break;
                case "delete": File.Delete(ResolvePath(args[0], installDirectory)); break;
                case "rmdir":
                    string directory = ResolvePath(args[^1], installDirectory);
                    if (Directory.Exists(directory))
                    {
                        bool recursive = args.Length == 2 && args[0] == "/r";
                        if (!recursive && Directory.EnumerateFileSystemEntries(directory).Any()) break;
                        if (recursive) RejectTreeLinks(directory);
                        Directory.Delete(directory, recursive);
                    }
                    break;
                default:
                    if (!OperatingSystem.IsWindows()) throw new PlatformNotSupportedException("Windows が必要です。");
                    ExecuteWindows(operation, installDirectory); break;
            }
        }
    }

    private static void RejectTreeLinks(string directory)
    {
        foreach (string child in Directory.EnumerateFileSystemEntries(directory))
        {
            FileAttributes attributes = File.GetAttributes(child);
            if ((attributes & FileAttributes.ReparsePoint) != 0) throw new IOException($"リンクを含むフォルダーは削除できません: {child}");
            if ((attributes & FileAttributes.Directory) != 0) RejectTreeLinks(child);
        }
    }

    [SupportedOSPlatform("windows")]
    private static void ExecuteWindows(ScriptOperation operation, string installDirectory)
    {
        string[] args = operation.Arguments;
        if (operation.Command == "createshortcut")
        {
            string shortcut = ResolvePath(args[0], installDirectory);
            Directory.CreateDirectory(Path.GetDirectoryName(shortcut)!);
            Type shellType = Type.GetTypeFromProgID("WScript.Shell") ?? throw new InvalidOperationException("ショートカットを作成できません。");
            object shell = Activator.CreateInstance(shellType)!;
            object? link = null;
            try
            {
                dynamic dynamicShell = shell;
                link = dynamicShell.CreateShortcut(shortcut);
                dynamic dynamicLink = link;
                dynamicLink.TargetPath = ResolvePath(args[1], installDirectory);
                dynamicLink.WorkingDirectory = Path.GetDirectoryName(dynamicLink.TargetPath);
                dynamicLink.Save();
            }
            finally { if (link is not null) Marshal.FinalReleaseComObject(link); Marshal.FinalReleaseComObject(shell); }
            return;
        }
        RegistryKey root = args[0] switch { "HKCU" => Registry.CurrentUser, "HKLM" => Registry.LocalMachine, _ => throw new InvalidDataException("不明なレジストリルートです。") };
        if (operation.Command == "deleteregkey") { root.DeleteSubKeyTree(args[1], false); return; }
        if (operation.Command is not ("writeregstr" or "writeregdword")) throw new InvalidDataException($"未対応の命令: {operation.Command}");
        using RegistryKey key = root.CreateSubKey(args[1]);
        if (operation.Command == "writeregdword") key.SetValue(args[2], unchecked((int)uint.Parse(args[3])), RegistryValueKind.DWord);
        else key.SetValue(args[2], ExpandValue(args[3], installDirectory), RegistryValueKind.String);
    }

    private static string ExpandValue(string value, string directory) => value.Replace("$INSTDIR", directory, StringComparison.OrdinalIgnoreCase)
        .Replace("$SMPROGRAMS", Environment.GetFolderPath(Environment.SpecialFolder.Programs), StringComparison.OrdinalIgnoreCase);

    public static string GetDefaultDirectory(PackageDefinition definition)
    {
        if (OperatingSystem.IsWindows() && definition.InstallDirectoryRegistry is string[] location)
        {
            RegistryKey root = location[0] == "HKCU" ? Registry.CurrentUser : Registry.LocalMachine;
            using RegistryKey? key = root.OpenSubKey(location[1]);
            if (key?.GetValue(location[2]) is string value && Path.IsPathFullyQualified(value)) return value;
        }
        return InstallPaths.Expand(definition.InstallDirectory);
    }
}
