using System.Diagnostics;
using System.Reflection;
using Hsis.Core;
using Microsoft.UI.Xaml;

namespace Hsis.App;

public sealed partial class InstallerWindow : Window
{
    private readonly PackageDefinition _definition;
    private int _page;
    private bool _installing;
    private string _installedDirectory = "";
    private readonly string? _uninstallDirectory;
    public InstallerWindow(PackageDefinition definition, string? uninstallDirectory = null)
    {
        _definition = definition;
        _uninstallDirectory = uninstallDirectory;
        InitializeComponent();
        WindowTools.Configure(this, 720, 620);
        Title = $"{definition.Name} セットアップ";
        AppName.Text = definition.Name;
        VersionLabel.Text = definition.Version;
        WelcomeText.Text = $"{definition.Name} のインストールをご案内します。";
        Terms.Text = definition.License;
        Destination.Text = RuntimeOperations.GetDefaultDirectory(definition);
        CompleteText.Text = $"{definition.Name} のインストールが完了しました。";
        LaunchApp.Visibility = string.IsNullOrWhiteSpace(definition.LaunchExecutable) ? Visibility.Collapsed : Visibility.Visible;
        if (uninstallDirectory is not null)
        {
            Title = $"{definition.Name} アンインストール";
            WelcomeText.Text = $"{definition.Name} をアンインストールします。";
            CompleteText.Text = "アンインストールが完了しました。";
            ProgressTitle.Text = "アンインストールしています";
            LaunchApp.Visibility = Visibility.Collapsed;
        }
        AppWindow.Closing += (_, e) => { if (_installing) e.Cancel = true; };
        ShowPage();
    }
    private void ShowPage()
    {
        FrameworkElement[] pages = [WelcomePage, LicensePage, DirectoryPage, ProgressPage, CompletePage];
        for (int index = 0; index < pages.Length; index++) pages[index].Visibility = index == _page ? Visibility.Visible : Visibility.Collapsed;
        StepLabel.Text = _page switch { 0 => "ようこそ", 1 => "ご利用の前に", 2 => "インストール設定", 3 => "処理中", _ => "完了" };
        BackButton.Visibility = _page is 1 or 2 ? Visibility.Visible : Visibility.Collapsed;
        NextButton.Content = _page switch { 2 => "インストール", 3 => "インストール中…", 4 => "完了", _ => "次へ" };
        if (_uninstallDirectory is not null && _page == 0) NextButton.Content = "アンインストール";
        NextButton.IsEnabled = _page != 3 && (_page != 1 || Agreement.IsChecked == true);
        CancelButton.Visibility = _page == 4 ? Visibility.Collapsed : Visibility.Visible;
        CancelButton.IsEnabled = _page != 3;
    }
    private void Agreement_Changed(object sender, RoutedEventArgs e) { if (_page == 1) NextButton.IsEnabled = Agreement.IsChecked == true; }
    private void Back_Click(object sender, RoutedEventArgs e) { _page = _page == 2 && string.IsNullOrWhiteSpace(_definition.License) ? 0 : _page - 1; ShowPage(); }
    private void Cancel_Click(object sender, RoutedEventArgs e) => Close();
    private async void Browse_Click(object sender, RoutedEventArgs e)
    {
        try { if (await WindowTools.PickFolderAsync(this) is string path) Destination.Text = path; }
        catch (Exception ex) { ShowError(ex.Message); }
    }
    private async void Next_Click(object sender, RoutedEventArgs e)
    {
        ErrorBar.IsOpen = false;
        if (_page == 4)
        {
            try
            {
                if (LaunchApp.Visibility == Visibility.Visible && LaunchApp.IsChecked == true)
                {
                    string executable = Path.GetFullPath(Path.Combine(_installedDirectory, _definition.LaunchExecutable));
                    string root = Path.TrimEndingDirectorySeparator(_installedDirectory) + Path.DirectorySeparatorChar;
                    if (!executable.StartsWith(root, StringComparison.OrdinalIgnoreCase)) throw new InvalidOperationException("起動ファイルはインストール先の中に指定してください。");
                    Process.Start(new ProcessStartInfo(executable) { UseShellExecute = true, WorkingDirectory = _installedDirectory });
                }
                Close();
            }
            catch (Exception ex) { ShowError(ex.Message); }
            return;
        }
        if (_page == 2) { await InstallAsync(); return; }
        if (_uninstallDirectory is not null && _page == 0) { await InstallAsync(); return; }
        if (_page == 1 && Agreement.IsChecked != true) return;
        _page = _page == 0 && string.IsNullOrWhiteSpace(_definition.License) ? 2 : _page + 1;
        ShowPage();
    }
    private async Task InstallAsync()
    {
        string destination = _uninstallDirectory ?? Destination.Text.Trim();
        if (!Path.IsPathFullyQualified(destination) || destination.Contains('"') || destination.Contains('\r') || destination.Contains('\n'))
        { ShowError("保存先には有効な絶対パスを指定してください。"); return; }
        _installing = true;
        _page = 3;
        ShowPage();
        try
        {
            using Stream resource = Assembly.GetExecutingAssembly().GetManifestResourceStream("Hsis.Payload.zip") ?? throw new InvalidDataException("インストールデータがありません。");
            Progress<string> progress = new(file => ProgressStatus.Text = $"配置: {file}");
            if (_uninstallDirectory is not null) await InstallationEngine.UninstallAsync(resource, destination, progress);
            else await InstallationEngine.InstallAsync(resource, destination, progress, setupExecutable: Environment.ProcessPath);
            _installedDirectory = Path.GetFullPath(destination);
            _page = 4;
        }
        catch (Exception ex) { _page = _uninstallDirectory is null ? 2 : 0; ShowError(ex.Message + " 一部の変更が適用済みの場合があります。"); }
        finally
        {
            _installing = false;
            ShowPage();
        }
    }
    private void ShowError(string message) { ErrorBar.Message = message; ErrorBar.IsOpen = true; }
}
