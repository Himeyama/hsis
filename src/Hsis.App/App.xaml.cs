using System.Reflection;
using System.Text.Json;
using Hsis.Core;
using System.Windows;

namespace Hsis.App;

public partial class App : Application
{
    private Window? _window;
    protected override void OnStartup(StartupEventArgs args)
    {
        base.OnStartup(args);
        using Stream? resource = Assembly.GetExecutingAssembly().GetManifestResourceStream("Hsis.Package.json");
        if (resource is null) { _window = new BuilderWindow(); _window.Show(); return; }
        PackageDefinition definition = JsonSerializer.Deserialize<PackageDefinition>(resource) ?? throw new InvalidDataException("セットアップ情報を読み込めません。");
        string[] arguments = Environment.GetCommandLineArgs().Skip(1).ToArray();
        string? uninstallDirectory = null;
        int rootArgument = Array.IndexOf(arguments, "--uninstall");
        if (rootArgument >= 0 && rootArgument + 1 < arguments.Length) uninstallDirectory = Path.GetFullPath(arguments[rootArgument + 1]);
        _window = new InstallerWindow(definition, uninstallDirectory);
        _window.Show();
    }
}
