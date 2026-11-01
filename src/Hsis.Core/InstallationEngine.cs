using System.IO.Compression;
using System.Text.Json;

namespace Hsis.Core;

public static class InstallationEngine
{
    public static async Task InstallAsync(Stream payload, string destination, IProgress<string>? progress = null, CancellationToken cancellationToken = default, Stream? uninstaller = null)
    {
        string root = Path.GetFullPath(destination);
        using ZipArchive archive = new(payload, ZipArchiveMode.Read, true);
        ZipArchiveEntry manifestEntry = archive.GetEntry("manifest.json") ?? throw new InvalidDataException("配置情報がありません。");
        await using Stream manifestStream = manifestEntry.Open();
        PayloadManifest manifest = await JsonSerializer.DeserializeAsync<PayloadManifest>(manifestStream, cancellationToken: cancellationToken).ConfigureAwait(false)
            ?? throw new InvalidDataException("配置情報が不正です。");
        RuntimeOperations.CheckRequirements(manifest.Initialize);
        RunningApplications.EnsureClosed(root);
        List<(ZipArchiveEntry Entry, string Target)> files = manifest.Files.Select(file =>
            (archive.GetEntry(file.Entry) ?? throw new InvalidDataException($"ファイルがありません: {file.Entry}"), Resolve(root, file.Destination))).ToList();
        List<string> directories = manifest.Directories.Select(directory => Resolve(root, directory)).ToList();
        // Validate the entire plan before writing any files.
        foreach (string directory in directories) { cancellationToken.ThrowIfCancellationRequested(); RejectLinks(directory); Directory.CreateDirectory(directory); }
        foreach ((ZipArchiveEntry entry, string target) in files)
        {
            cancellationToken.ThrowIfCancellationRequested();
            RejectLinks(target);
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            progress?.Report(Path.GetRelativePath(root, target));
            await using Stream source = entry.Open();
            await using FileStream file = new(target, FileMode.Create, FileAccess.Write, FileShare.None);
            await source.CopyToAsync(file, cancellationToken).ConfigureAwait(false);
        }
        RuntimeOperations.Execute(manifest.Install, root, uninstaller, progress);
    }

    public static async Task UninstallAsync(Stream payload, string destination, IProgress<string>? progress = null)
    {
        using ZipArchive archive = new(payload, ZipArchiveMode.Read, true);
        await using Stream metadata = (archive.GetEntry("manifest.json") ?? throw new InvalidDataException("配置情報がありません。")).Open();
        PayloadManifest manifest = await JsonSerializer.DeserializeAsync<PayloadManifest>(metadata).ConfigureAwait(false) ?? throw new InvalidDataException("配置情報が不正です。");
        string root = Path.GetFullPath(destination);
        RunningApplications.EnsureClosed(root);
        RuntimeOperations.Execute(manifest.Uninstall, root, null, progress);
    }

    private static string Resolve(string root, string relative)
    {
        if (Path.IsPathRooted(relative) || relative.Contains(':')) throw new InvalidDataException("配置先が不正です。");
        string path = Path.GetFullPath(Path.Combine(root, relative));
        if (!path.Equals(root, StringComparison.OrdinalIgnoreCase) && !path.StartsWith(Path.TrimEndingDirectorySeparator(root) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("配置先がインストールフォルダーの外です。");
        RejectLinks(path);
        return path;
    }

    internal static void RejectLinks(string path)
    {
        string? current = path;
        while (current is not null)
        {
            if ((File.Exists(current) || Directory.Exists(current)) && (File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0)
                throw new IOException($"リンク先への配置には対応していません: {current}");
            current = Path.GetDirectoryName(current);
        }
    }
}
