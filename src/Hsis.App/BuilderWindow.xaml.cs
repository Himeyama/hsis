using System.Diagnostics;
using Hsis.Core;
using Microsoft.UI.Xaml;

namespace Hsis.App;

public sealed partial class BuilderWindow : Window
{
    private string? _setup;
    private bool _building;
    public BuilderWindow()
    {
        InitializeComponent();
        WindowTools.Configure(this, 940, 920);
        OutputPath.Text = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "HSIS", "Output");
        InstallPath.Text = @"$LOCALAPPDATA\Programs\My Application";
        AppWindow.Closing += (_, e) => { if (_building) e.Cancel = true; };
    }
    private async void PickScript_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            if (await WindowTools.PickFileAsync(this, ".nsi") is not string path) return;
            ScriptPath.Text = path;
            LoadDefinition();
        }
        catch (Exception ex) { Status.Text = "定義を読み込めませんでした"; BuildLog.Text = ex.Message; }
    }
    private Dictionary<string, string> GetConstants() => DefinitionConstants.Parse(BuildConstants.Text.Split('\n'));
    private void ReadDefinition_Click(object sender, RoutedEventArgs e)
    {
        try { LoadDefinition(); }
        catch (Exception ex) { Status.Text = "定義を読み込めませんでした"; BuildLog.Text = ex.Message; }
    }
    private void LoadDefinition()
    {
        InstallPlan plan = new DefinitionCompiler().Compile(ScriptPath.Text.Trim(), GetConstants());
        ProductName.Text = plan.Definition.Name; ProductVersion.Text = plan.Definition.Version;
        InstallPath.Text = plan.Definition.InstallDirectory; LicenseText.Text = plan.Definition.License;
        LaunchPath.Text = plan.Definition.LaunchExecutable; AdminRequired.IsChecked = plan.Definition.RequiresAdministrator;
        Status.Text = "定義を読み込みました"; BuildLog.Text = "";
    }
    private async void PickOutput_Click(object sender, RoutedEventArgs e) { if (await WindowTools.PickFolderAsync(this) is string path) OutputPath.Text = path; }
    private async void PickLicense_Click(object sender, RoutedEventArgs e)
    {
        try { if (await WindowTools.PickFileAsync(this, ".txt", ".md") is string path) LicenseText.Text = await File.ReadAllTextAsync(path); }
        catch (Exception ex) { Status.Text = ex.Message; }
    }
    private async void Build_Click(object sender, RoutedEventArgs e)
    {
        _building = true;
        BuildButton.IsEnabled = OpenSetup.IsEnabled = false;
        BuildProgress.Visibility = Visibility.Visible;
        BuildLog.Text = "";
        Status.Text = "作成しています…";
        // Snapshot every input before awaiting so edits cannot change an in-flight build.
        string script = ScriptPath.Text.Trim();
        string output = OutputPath.Text.Trim();
        try
        {
            if (string.IsNullOrWhiteSpace(output)) throw new InvalidOperationException("出力先を指定してください。");
            Dictionary<string, string> defines = GetConstants();
            PackageDefinition definition = new DefinitionCompiler().Compile(script, defines).Definition with
            {
                Name = ProductName.Text.Trim(), Version = ProductVersion.Text.Trim(), InstallDirectory = InstallPath.Text.Trim(),
                License = LicenseText.Text, LaunchExecutable = LaunchPath.Text.Trim(), RequiresAdministrator = AdminRequired.IsChecked == true
            };
            _setup = await new PackageBuilder().BuildAsync(script, output, Path.Combine(AppContext.BaseDirectory, "Template"), definition,
                line => DispatcherQueue.TryEnqueue(() => BuildLog.Text += line + Environment.NewLine), defines: defines);
            Status.Text = "作成完了。セットアップを起動して確認できます。";
            OpenSetup.IsEnabled = true;
        }
        catch (Exception ex) { Status.Text = "作成できませんでした"; BuildLog.Text += ex.Message + Environment.NewLine; }
        finally { _building = false; BuildButton.IsEnabled = true; BuildProgress.Visibility = Visibility.Collapsed; }
    }
    private void OpenSetup_Click(object sender, RoutedEventArgs e)
    {
        try { if (_setup is not null) Process.Start(new ProcessStartInfo(_setup) { UseShellExecute = true }); }
        catch (Exception ex) { Status.Text = ex.Message; }
    }
}
