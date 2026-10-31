namespace Hsis.Core;

public static class InstallPaths
{
    public static string Expand(string value) => value
        .Replace("$LOCALAPPDATA", Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), StringComparison.OrdinalIgnoreCase)
        .Replace("$PROGRAMFILES64", Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), StringComparison.OrdinalIgnoreCase)
        .Replace("$PROGRAMFILES", Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), StringComparison.OrdinalIgnoreCase)
        .Replace("$APPDATA", Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), StringComparison.OrdinalIgnoreCase);
}
