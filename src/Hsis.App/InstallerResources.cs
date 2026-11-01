using System.Reflection;
using Hsis.Core;

namespace Hsis.App;

internal static class InstallerResources
{
    private static string PackagePath
    {
        get
        {
            string[] arguments = Environment.GetCommandLineArgs();
            int index = Array.IndexOf(arguments, "--package");
            return index >= 0 && index + 1 < arguments.Length ? arguments[index + 1]
                : Environment.ProcessPath ?? throw new InvalidOperationException("セットアップの場所を取得できません。");
        }
    }

    public static Stream OpenPayload() => (Environment.GetCommandLineArgs().Contains("--uninstall")
        ? Assembly.GetExecutingAssembly().GetManifestResourceStream("Hsis.UninstallerPayload") : null)
        ?? Assembly.GetExecutingAssembly().GetManifestResourceStream("Hsis.Payload.zip") ?? SetupArchive.OpenPayload(PackagePath);

    public static Stream? OpenUninstaller() => SetupArchive.OpenUninstaller(PackagePath);
}
