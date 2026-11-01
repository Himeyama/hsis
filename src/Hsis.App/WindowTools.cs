using Microsoft.UI.Xaml;
using System.Runtime.InteropServices;
using Windows.Graphics;
using Windows.Storage.Pickers;
using WinRT.Interop;

namespace Hsis.App;

internal static class WindowTools
{
    [DllImport("user32.dll", ExactSpelling = true)]
    private static extern uint GetDpiForWindow(nint window);

    public static void ResizeForDisplayScale(Window window, int width, int height)
    {
        double scale = GetDpiForWindow(WindowNative.GetWindowHandle(window)) / 96.0;
        window.AppWindow.Resize(new SizeInt32(
            (int)Math.Round(width * scale),
            (int)Math.Round(height * scale)));
    }

    public static void UseCustomTitleBar(Window window, FrameworkElement titleBar)
    {
        window.ExtendsContentIntoTitleBar = true;
        window.SetTitleBar(titleBar);
        window.AppWindow.TitleBar.ButtonBackgroundColor = Microsoft.UI.Colors.Transparent;
        window.AppWindow.TitleBar.ButtonInactiveBackgroundColor = Microsoft.UI.Colors.Transparent;
        void UpdateColors()
        {
            window.AppWindow.TitleBar.ButtonForegroundColor = titleBar.ActualTheme == ElementTheme.Dark ? Microsoft.UI.Colors.White : Microsoft.UI.Colors.Black;
            window.AppWindow.TitleBar.ButtonInactiveForegroundColor = Microsoft.UI.Colors.Gray;
        }
        titleBar.ActualThemeChanged += (_, _) => UpdateColors();
        UpdateColors();
    }
    public static void Configure(Window window)
    {
        window.AppWindow.SetIcon(Path.Combine(AppContext.BaseDirectory, "Hsis.ico"));
        window.SystemBackdrop = new Microsoft.UI.Xaml.Media.MicaBackdrop();
    }
    public static async Task<string?> PickFileAsync(Window window, params string[] extensions)
    {
        FileOpenPicker picker = new();
        foreach (string extension in extensions) picker.FileTypeFilter.Add(extension);
        InitializeWithWindow.Initialize(picker, WindowNative.GetWindowHandle(window));
        Windows.Storage.StorageFile? file = await picker.PickSingleFileAsync();
        return file?.Path;
    }
    public static async Task<string?> PickFolderAsync(Window window)
    {
        FolderPicker picker = new();
        picker.FileTypeFilter.Add("*");
        InitializeWithWindow.Initialize(picker, WindowNative.GetWindowHandle(window));
        Windows.Storage.StorageFolder? folder = await picker.PickSingleFolderAsync();
        return folder?.Path;
    }
}
