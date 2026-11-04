using SharpCompress.Archives;
using SharpCompress.Readers;

namespace Hsis.Core;

public static class SevenZip
{
    public static string Executable => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "tar.exe");

    public static Task CreateAsync(string source, string archive, Action<string> log, CancellationToken cancellationToken = default)
    {
        string? sevenZip = Environment.GetEnvironmentVariable("HSIS_7ZIP_PATH");
        return string.IsNullOrWhiteSpace(sevenZip)
            ? ProcessRunner.RunAsync(Executable, ["-c", "--format=7zip", "--options=7zip:compression=lzma2,7zip:compression-level=9", "-f", Path.GetFullPath(archive), "."], source, log, cancellationToken)
            : ProcessRunner.RunAsync(sevenZip, ["a", "-t7z", "-m0=lzma2", "-mx=9", Path.GetFullPath(archive), ".\\*"], source, log, cancellationToken);
    }

    public static async Task ExtractAsync(string archive, string destination, string selection, Action<string> log, CancellationToken cancellationToken = default)
    {
        string? sevenZip = Environment.GetEnvironmentVariable("HSIS_7ZIP_PATH");
        if (!string.IsNullOrWhiteSpace(sevenZip))
        {
            await ProcessRunner.RunAsync(sevenZip, ["x", "-y", "-scsUTF-8", $"-o{Path.GetFullPath(destination)}", Path.GetFullPath(archive), $"@{Path.GetFullPath(selection)}"], destination, log, cancellationToken).ConfigureAwait(false);
            return;
        }
        HashSet<string> selected = new(await File.ReadAllLinesAsync(selection, cancellationToken).ConfigureAwait(false), StringComparer.Ordinal);
        string root = Path.GetFullPath(destination);
        using IArchive input = ArchiveFactory.OpenArchive(archive);
        using IReader reader = input.ExtractAllEntries();
        while (reader.MoveToNextEntry())
        {
            cancellationToken.ThrowIfCancellationRequested();
            string name = (reader.Entry.Key ?? "").Replace('\\', '/');
            if (name.StartsWith("./", StringComparison.Ordinal)) name = name[2..];
            if (reader.Entry.IsDirectory || !selected.Remove(name)) continue;
            if (!name.StartsWith("files/", StringComparison.Ordinal) || !int.TryParse(name.AsSpan(6), out int index) || index < 0 || name != $"files/{index}")
                throw new InvalidDataException("圧縮データのファイル名が不正です。");
            string path = Path.Combine(root, "files", index.ToString());
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            InstallationEngine.RejectLinks(path);
            using Stream source = reader.OpenEntryStream();
            await using FileStream output = new(path, FileMode.CreateNew, FileAccess.Write, FileShare.None);
            await source.CopyToAsync(output, cancellationToken).ConfigureAwait(false);
        }
        if (selected.Count != 0) throw new InvalidDataException("圧縮データに必要なファイルがありません。");
    }
}
