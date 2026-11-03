using System.Diagnostics;
using Hsis.Core;
using System.Windows;

namespace Hsis.App;

public sealed partial class BuilderWindow : Window
{
    private string? _setup;
    private bool _building;
    private string? _loadedInputs;
    private bool _definitionReady;
    private List<string> _missingConstants = [];
    public BuilderWindow()
    {
        InitializeComponent();
        WindowTools.Configure(this);
        OutputPath.Text = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "HSIS", "Output");
        InstallPath.Text = @"$LOCALAPPDATA\Programs\My Application";
        Closing += (_, e) => { if (_building) e.Cancel = true; };
    }
    private async void PickScript_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            if (await WindowTools.PickFileAsync(this, ".nsi") is not string path) return;
            ScriptPath.Text = path;
            LoadDefinition();
        }
        catch (Exception ex) { ShowDefinitionError(ex); }
    }
    private Dictionary<string, string> GetConstants() => DefinitionConstants.ParseValues(BuildConstants.Text);
    private void ReadDefinition_Click(object sender, RoutedEventArgs e)
    {
        try { LoadDefinition(); }
        catch (Exception ex) { ShowDefinitionError(ex); }
    }
    private void LoadDefinition()
    {
        _definitionReady = false;
        _loadedInputs = null;
        DefinitionInspection inspection = new DefinitionCompiler().Inspect(ScriptPath.Text.Trim(), GetConstants());
        BuildConstants.Text = DefinitionConstants.AddMissing(BuildConstants.Text, inspection.MissingConstants);
        ProductName.Text = inspection.Definition.Name; ProductVersion.Text = inspection.Definition.Version;
        InstallPath.Text = inspection.Definition.InstallDirectory; LicenseText.Text = inspection.Definition.License;
        LaunchPath.Text = inspection.Definition.LaunchExecutable; AdminRequired.IsChecked = inspection.Definition.RequiresAdministrator;
        _loadedInputs = ScriptPath.Text.Trim() + "\n" + BuildConstants.Text;
        _definitionReady = inspection.MissingConstants.Count == 0;
        _missingConstants = inspection.MissingConstants;
        DefinitionNotice.IsOpen = false;
        Status.Text = "定義を読み込みました";
        BuildLog.Text = "";
    }
    private void ShowDefinitionError(Exception error)
    {
        _definitionReady = false;
        _loadedInputs = null;
        Status.Text = "定義を読み込めませんでした";
        DefinitionNotice.Title = "定義を読み込めませんでした";
        DefinitionDetails.Text = error.Message;
        DefinitionNotice.IsOpen = true;
        BuildLog.Text = error.Message;
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
            if (_loadedInputs != script + "\n" + BuildConstants.Text) LoadDefinition();
            if (!_definitionReady) throw new InvalidOperationException("未指定のビルド定数: " + string.Join(", ", _missingConstants));
            Dictionary<string, string> defines = GetConstants();
            PackageDefinition definition = new DefinitionCompiler().Compile(script, defines).Definition with
            {
                Name = ProductName.Text.Trim(), Version = ProductVersion.Text.Trim(), InstallDirectory = InstallPath.Text.Trim(),
                License = LicenseText.Text, LaunchExecutable = LaunchPath.Text.Trim(), RequiresAdministrator = AdminRequired.IsChecked == true
            };
            _setup = await new PackageBuilder().BuildAsync(script, output, Path.Combine(AppContext.BaseDirectory, "Template"), definition,
                line => Dispatcher.InvokeAsync(() => BuildLog.Text += line + Environment.NewLine), defines: defines);
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
