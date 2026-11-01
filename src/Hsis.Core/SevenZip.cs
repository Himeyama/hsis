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

    public static Task ExtractAsync(string archive, string destination, string selection, Action<string> log, CancellationToken cancellationToken = default)
    {
        string? sevenZip = Environment.GetEnvironmentVariable("HSIS_7ZIP_PATH");
        return string.IsNullOrWhiteSpace(sevenZip)
            ? ProcessRunner.RunAsync(Executable, ["-x", "-f", archive, "-C", destination, "-T", selection], destination, log, cancellationToken)
            : ProcessRunner.RunAsync(sevenZip, ["x", "-y", "-scsUTF-8", $"-o{Path.GetFullPath(destination)}", Path.GetFullPath(archive), $"@{Path.GetFullPath(selection)}"], destination, log, cancellationToken);
    }
}
