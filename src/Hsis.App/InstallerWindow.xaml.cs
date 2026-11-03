using System.Diagnostics;
using System.Reflection;
using Hsis.Core;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media.Imaging;

namespace Hsis.App;

public sealed partial class InstallerWindow : Window
{
    private readonly PackageDefinition _definition;
    private int _page;
    private bool _installing;
    private string _installedDirectory = "";
    private readonly string? _uninstallDirectory;
    private bool _confirmingClose;
    private bool _allowClose;
    private IInputElement? _previousFocus;
    public InstallerWindow(PackageDefinition definition, string? uninstallDirectory = null)
    {
        _definition = definition;
        _uninstallDirectory = uninstallDirectory;
        InitializeComponent();
        WindowTools.Configure(this);
        Title = $"{definition.Name} セットアップ";
        AppName.Text = definition.Name;
        VersionLabel.Text = definition.Version;
        Terms.Text = definition.License;
        Destination.Text = RuntimeOperations.GetDefaultDirectory(definition);
        CompleteText.Text = $"{definition.Name} のインストールが完了しました。";
        LaunchApp.Visibility = string.IsNullOrWhiteSpace(definition.LaunchExecutable) ? Visibility.Collapsed : Visibility.Visible;
        if (uninstallDirectory is not null)
        {
            Title = $"{definition.Name} アンインストール";
            WelcomeTitle.Text = "アンインストール";
            CompleteTitle.Text = "アンインストールが完了しました";
            CompleteText.Text = "アンインストールが完了しました。";
            ProgressTitle.Text = "アンインストールしています";
            LaunchApp.Visibility = Visibility.Collapsed;
        }
        Closing += (_, e) =>
        {
            if (_allowClose || _page == 4) return;
            e.Cancel = true;
            if (!_installing) ConfirmClose();
        };
        PreviewKeyDown += (_, e) =>
        {
            if (_confirmingClose && e.Key == Key.Escape)
            {
                DismissCloseConfirmation();
                e.Handled = true;
            }
        };
        ShowPage();
        LoadIcon();
    }
    private void LoadIcon()
    {
        try
        {
            using Stream? icon = Assembly.GetExecutingAssembly().GetManifestResourceStream("Hsis.AppIcon.png");
            if (icon is null) return;
            BitmapImage image = new();
            image.BeginInit();
            image.CacheOption = BitmapCacheOption.OnLoad;
            image.StreamSource = icon;
            image.EndInit();
            image.Freeze();
            ProductIcon.Source = image;
            ProductIcon.Visibility = Visibility.Visible;
            FallbackIcon.Visibility = Visibility.Collapsed;
        }
        catch (Exception ex) when (ex is IOException or ArgumentException or System.Runtime.InteropServices.COMException)
        {
            // An unreadable optional icon must not prevent setup from opening.
            System.Diagnostics.Debug.WriteLine(ex.Message);
        }
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
    private void Cancel_Click(object sender, RoutedEventArgs e) => ConfirmClose();
    private void ConfirmClose()
    {
        if (_installing || _confirmingClose) return;
        _confirmingClose = true;
        _previousFocus = Keyboard.FocusedElement;
        CloseConfirmationTitle.Text = _uninstallDirectory is null ? "セットアップを中止しますか？" : "アンインストールを中止しますか？";
        SetupContent.IsEnabled = false;
        CloseConfirmation.Visibility = Visibility.Visible;
        ContinueButton.Focus();
    }
    private void Continue_Click(object sender, RoutedEventArgs e) => DismissCloseConfirmation();
    private void DismissCloseConfirmation()
    {
        CloseConfirmation.Visibility = Visibility.Collapsed;
        SetupContent.IsEnabled = true;
        _confirmingClose = false;
        if (_previousFocus is not null) Keyboard.Focus(_previousFocus);
    }
    private void Abort_Click(object sender, RoutedEventArgs e)
    {
        _allowClose = true;
        Close();
    }
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
        await Task.Yield();
        try
        {
            using Stream resource = InstallerResources.OpenPayload();
            using Stream? uninstaller = _uninstallDirectory is null ? InstallerResources.OpenUninstaller() : null;
            Progress<string> progress = new(message => ProgressStatus.Text = message);
            if (_uninstallDirectory is not null) await InstallationEngine.UninstallAsync(resource, destination, progress);
            else await InstallationEngine.InstallAsync(resource, destination, progress, uninstaller: uninstaller);
            _installedDirectory = Path.GetFullPath(destination);
            _page = 4;
        }
        catch (ApplicationsInUseException ex) { _page = _uninstallDirectory is null ? 2 : 0; ShowError(ex.Message); }
        catch (Exception ex) { _page = _uninstallDirectory is null ? 2 : 0; ShowError(ex.Message + " 一部の変更が適用済みの場合があります。"); }
        finally
        {
            _installing = false;
            ShowPage();
        }
    }
    private void ShowError(string message) { ErrorBar.Message = message; ErrorBar.IsOpen = true; }
}
