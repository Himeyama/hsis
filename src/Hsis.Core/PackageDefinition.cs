namespace Hsis.Core;

public sealed record PackageDefinition
{
    public string Name { get; init; } = "My Application";
    public string Version { get; init; } = "1.0.0";
    public string InstallDirectory { get; init; } = "";
    public string License { get; init; } = "";
    public string LaunchExecutable { get; init; } = "";
    public bool RequiresAdministrator { get; init; }
    public string ProductVersion { get; init; } = "1.0.0.0";
    public Dictionary<string, string> VersionKeys { get; init; } = new(StringComparer.OrdinalIgnoreCase);
    public string[]? InstallDirectoryRegistry { get; init; }
    public string Uninstaller { get; init; } = "";
}
