using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using System.Windows.Shell;
using System.Windows.Interop;
using Hsis.App;
using Hsis.Core;

namespace Hsis.WpfChecks;

internal static class Program
{
    private static bool _headless;

    [STAThread]
    private static void Main(string[] args)
    {
        _headless = args.Contains("--ci");
        Hsis.App.App application = new();
        application.InitializeComponent();
#pragma warning disable WPF0001
        application.ThemeMode = args.Contains("--dark") ? ThemeMode.Dark : ThemeMode.Light;
#pragma warning restore WPF0001
        application.ShutdownMode = ShutdownMode.OnExplicitShutdown;
        if (args.Contains("--check-dialog-surface"))
        {
            InstallerWindow probe = new(new PackageDefinition { Name = "Hello App" });
            probe.ApplyTemplate();
            probe.Measure(new Size(probe.Width, probe.Height));
            probe.Arrange(new Rect(0, 0, probe.Width, probe.Height));
            ((Button)probe.FindName("CancelButton")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            probe.UpdateLayout();
            probe.Dispatcher.Invoke(() => { }, DispatcherPriority.ApplicationIdle);
            Border surface = (Border)probe.FindName("DialogSurface");
            surface.GetBindingExpression(Border.BackgroundProperty)!.UpdateTarget();
            Check(surface.Background is SolidColorBrush brush && brush.Color.A == 255 && brush.Opacity == 1,
                "確認ダイアログの背景が不透明である");
            Check(probe.WindowStyle == WindowStyle.None && WindowChrome.GetWindowChrome(probe)?.CaptionHeight == 32,
                "共通カスタムタイトルバーを使用する");
            ((Button)probe.FindName("AbortButton")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            application.Shutdown();
            return;
        }
        string output = Path.GetFullPath("artifacts/wpf-checks/" + (args.Contains("--dark") ? "dark" : "light"));
        Directory.CreateDirectory(output);
        BuilderWindow studio = new();
        PrepareWindow(studio);
        Capture(studio, Path.Combine(output, "studio.png"));
        studio.Close();
        PackageDefinition definition = new() { Name = "Hello App", Version = "1.0.0", License = "サンプルの利用規約です。\n内容を確認して同意してください。" };
        InstallerWindow installer = new(definition);
        bool installerClosed = false;
        installer.Closed += (_, _) => installerClosed = true;
        PrepareWindow(installer);
        Capture(installer, Path.Combine(output, "welcome.png"));
        Button next = (Button)installer.FindName("NextButton");
        next.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Check(!next.IsEnabled, "利用規約への同意前は次へ進めない");
        ((CheckBox)installer.FindName("Agreement")).IsChecked = true;
        Check(next.IsEnabled, "同意すると次へ進める");
        Capture(installer, Path.Combine(output, "license.png"));
        next.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Check((string)next.Content == "インストール", "保存先ページへ進む");
        Capture(installer, Path.Combine(output, "directory.png"));
        ((TextBox)installer.FindName("Destination")).Text = "relative";
        next.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Check(((Notice)installer.FindName("ErrorBar")).IsOpen, "無効な保存先を画面で案内する");
        Capture(installer, Path.Combine(output, "invalid-directory.png"));
        int windowsBeforeConfirmation = application.Windows.Count;
        ((Button)installer.FindName("CancelButton")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Check(((FrameworkElement)installer.FindName("CloseConfirmation")).Visibility == Visibility.Visible
            && !((FrameworkElement)installer.FindName("SetupContent")).IsEnabled
            && Window.GetWindow((FrameworkElement)installer.FindName("CloseConfirmation")) == installer
            && application.Windows.Count == windowsBeforeConfirmation, "中止確認は同じウィンドウ内に表示し背景操作を止める");
        Check(((Button)installer.FindName("ContinueButton")).IsDefault, "続けるを既定の操作にする");
        if (!_headless) Check(((Button)installer.FindName("ContinueButton")).IsKeyboardFocused, "続けるを初期選択にする");
        Check(((Border)installer.FindName("DialogSurface")).Background is SolidColorBrush dialogBrush
            && dialogBrush.Color.A == 255 && dialogBrush.Opacity == 1, "確認ダイアログの背景が透けない");
        Capture(installer, Path.Combine(output, "cancel-confirmation.png"));
        ((Button)installer.FindName("ContinueButton")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Check(((FrameworkElement)installer.FindName("CloseConfirmation")).Visibility == Visibility.Collapsed
            && ((FrameworkElement)installer.FindName("SetupContent")).IsEnabled, "続けるで元の画面へ戻る");
        installer.Close();
        Check(!installerClosed && ((FrameworkElement)installer.FindName("CloseConfirmation")).Visibility == Visibility.Visible,
            "タイトルバーから閉じてもアプリ内で確認する");
        ((Button)installer.FindName("AbortButton")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Check(installerClosed, "中止するを選ぶとウィンドウを閉じる");
        InstallerWindow removal = new(definition, output);
        PrepareWindow(removal);
        Check((string)((Button)removal.FindName("NextButton")).Content == "アンインストール", "削除モードで確認画面を開く");
        Capture(removal, Path.Combine(output, "uninstall.png"));
        removal.Close();
        Check(((TextBlock)removal.FindName("CloseConfirmationTitle")).Text == "アンインストールを中止しますか？",
            "削除モードでは削除の中止を確認する");
        ((Button)removal.FindName("AbortButton")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        application.Shutdown();
    }

    private static void PrepareWindow(Window window)
    {
        if (!_headless)
        {
            window.Show();
            return;
        }
        // CI has no foreground desktop. Create the native window without showing
        // it, so closing events, layout and bindings can still be exercised.
        _ = new WindowInteropHelper(window).EnsureHandle();
        window.ApplyTemplate();
        window.Measure(new Size(window.Width, window.Height));
        window.Arrange(new Rect(0, 0, window.Width, window.Height));
    }

    private static void Capture(Window window, string path)
    {
        window.Dispatcher.Invoke(() => { }, DispatcherPriority.ApplicationIdle);
        window.UpdateLayout();
        if (_headless)
        {
            FrameworkElement content = (FrameworkElement)window.Content;
            content.Measure(new Size(window.Width, window.Height));
            content.Arrange(new Rect(0, 0, window.Width, window.Height));
            content.UpdateLayout();
            Check(content.ActualWidth > 0 && content.ActualHeight > 0, "画面表示なしでレイアウトを検証: " + Path.GetFileNameWithoutExtension(path));
            return;
        }
        RenderTargetBitmap bitmap = new((int)window.ActualWidth, (int)window.ActualHeight, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(window);
        PngBitmapEncoder encoder = new();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using FileStream file = File.Create(path);
        encoder.Save(file);
        Console.WriteLine("PASS: " + Path.GetFileName(path));
    }

    private static void Check(bool condition, string label)
    {
        if (!condition) throw new InvalidOperationException(label);
        Console.WriteLine("PASS: " + label);
    }
}
