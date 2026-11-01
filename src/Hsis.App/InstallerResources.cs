using System.Reflection;
using Hsis.Core;

namespace Hsis.App;

internal static class InstallerResources
{
    public static Stream OpenPayload() => Assembly.GetExecutingAssembly().GetManifestResourceStream("Hsis.Payload.zip")
        ?? SetupArchive.OpenPayload(Environment.ProcessPath ?? throw new InvalidOperationException("セットアップの場所を取得できません。"));

    public static Stream? OpenUninstaller() => SetupArchive.OpenUninstaller(Environment.ProcessPath ?? throw new InvalidOperationException("セットアップの場所を取得できません。"));
}
