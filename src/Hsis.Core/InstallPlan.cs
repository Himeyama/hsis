namespace Hsis.Core;

public sealed record InstallFile(string Source, string Destination);
public sealed record InstallPlan(PackageDefinition Definition, string OutputFile, List<InstallFile> Files, List<string> Directories)
{
    public List<ScriptOperation> Initialize { get; init; } = [];
    public List<ScriptOperation> Install { get; init; } = [];
    public List<ScriptOperation> Uninstall { get; init; } = [];
}
public sealed record ScriptOperation(string Command, string[] Arguments);
public sealed record PayloadFile(string Entry, string Destination);
public sealed record PayloadManifest(List<PayloadFile> Files, List<string> Directories)
{
    public List<ScriptOperation> Initialize { get; init; } = [];
    public List<ScriptOperation> Install { get; init; } = [];
    public List<ScriptOperation> Uninstall { get; init; } = [];
}
