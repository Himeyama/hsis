using System.Reflection;
using System.Text.Json;
using System.Diagnostics;
using Hsis.Core;
using Microsoft.UI.Xaml;

namespace Hsis.App;

public partial class App : Application
{
    private Window? _window;
    public App() => InitializeComponent();
    protected override async void OnLaunched(LaunchActivatedEventArgs args)
    {
        using Stream? resource = Assembly.GetExecutingAssembly().GetManifestResourceStream("Hsis.Package.json");
        if (resource is null) { _window = new BuilderWindow(); _window.Activate(); return; }
        PackageDefinition definition = JsonSerializer.Deserialize<PackageDefinition>(resource) ?? throw new InvalidDataException("セットアップ情報を読み込めません。");
        string[] arguments = Environment.GetCommandLineArgs().Skip(1).ToArray();
        string? uninstallDirectory = null;
        int rootArgument = Array.IndexOf(arguments, "--uninstall");
        if (rootArgument >= 0 && rootArgument + 1 < arguments.Length) uninstallDirectory = Path.GetFullPath(arguments[rootArgument + 1]);
        else if (definition.Uninstaller.Length > 0 && string.Equals(Path.GetFileName(Environment.ProcessPath), Path.GetFileName(definition.Uninstaller), StringComparison.OrdinalIgnoreCase))
        {
            // Run a temporary copy so the installed uninstaller can be removed as part of its own section.
            string source = Environment.ProcessPath!;
            string installRoot = Path.GetDirectoryName(source) ?? throw new InvalidDataException("アンインストーラーの場所が不正です。");
            string? uninstallerParent = Path.GetDirectoryName(definition.Uninstaller);
            if (!string.IsNullOrEmpty(uninstallerParent))
                foreach (string _ in uninstallerParent.Split(Path.DirectorySeparatorChar)) installRoot = Path.GetDirectoryName(installRoot) ?? throw new InvalidDataException("インストール先を取得できません。");
            string temporary = Path.Combine(Path.GetTempPath(), "Hsis", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(temporary);
            string executable = Path.Combine(temporary, "Uninstall.exe");
            File.Copy(source, executable);
            ProcessStartInfo start = new(executable) { UseShellExecute = true };
            start.ArgumentList.Add("--uninstall"); start.ArgumentList.Add(installRoot);
            start.ArgumentList.Add("--wait-pid"); start.ArgumentList.Add(Environment.ProcessId.ToString());
            if (arguments.Contains("/S", StringComparer.OrdinalIgnoreCase)) start.ArgumentList.Add("--quiet");
            Process.Start(start); Exit(); return;
        }
        int waitArgument = Array.IndexOf(arguments, "--wait-pid");
        if (waitArgument >= 0 && waitArgument + 1 < arguments.Length && int.TryParse(arguments[waitArgument + 1], out int parentId))
        {
            try { using Process parent = Process.GetProcessById(parentId); await parent.WaitForExitAsync(); }
            catch (ArgumentException) { }
        }
        if (uninstallDirectory is not null && arguments.Contains("--quiet"))
        {
            try
            {
                using Stream payload = Assembly.GetExecutingAssembly().GetManifestResourceStream("Hsis.Payload.zip")!;
                await InstallationEngine.UninstallAsync(payload, uninstallDirectory);
                Environment.ExitCode = 0;
            }
            catch { Environment.ExitCode = 1; }
            Exit(); return;
        }
        _window = new InstallerWindow(definition, uninstallDirectory);
        _window.Activate();
    }
}
