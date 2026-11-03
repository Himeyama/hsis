using System.Windows;
using System.Windows.Controls;
using System.Windows.Media.Imaging;
using System.Windows.Shell;
using Microsoft.Win32;

namespace Hsis.App;

internal static class WindowTools
{
    public static void Configure(Window window)
    {
        string assembly = typeof(WindowTools).Assembly.GetName().Name!;
        window.Icon = BitmapFrame.Create(new Uri($"pack://application:,,,/{assembly};component/Hsis.ico"));
        window.WindowStartupLocation = WindowStartupLocation.CenterScreen;
        window.WindowStyle = WindowStyle.None;
        WindowChrome.SetWindowChrome(window, new WindowChrome
        {
            CaptionHeight = 32,
            ResizeBorderThickness = new Thickness(6),
            GlassFrameThickness = new Thickness(0),
            UseAeroCaptionButtons = false
        });
        UIElement content = (UIElement)window.Content;
        window.Content = null;
        Grid frame = new();
        frame.RowDefinitions.Add(new RowDefinition { Height = new GridLength(32) });
        frame.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        frame.Children.Add(new TitleBar());
        Grid.SetRow(content, 1);
        frame.Children.Add(content);
        window.Content = frame;
        window.StateChanged += (_, _) => frame.Margin = window.WindowState == WindowState.Maximized ? new Thickness(8) : new Thickness(0);
    }
    public static Task<string?> PickFileAsync(Window window, params string[] extensions)
    {
        OpenFileDialog picker = new() { Filter = "対応ファイル|" + string.Join(";", extensions.Select(extension => "*" + extension)) };
        return Task.FromResult(picker.ShowDialog(window) == true ? picker.FileName : null);
    }
    public static Task<string?> PickFolderAsync(Window window)
    {
        OpenFolderDialog picker = new();
        return Task.FromResult(picker.ShowDialog(window) == true ? picker.FolderName : null);
    }
}
