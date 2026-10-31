using Microsoft.UI.Xaml;
using Windows.Storage.Pickers;
using WinRT.Interop;

namespace Hsis.App;

internal static class WindowTools
{
    public static void Configure(Window window, int width, int height)
    {
        window.SystemBackdrop = new Microsoft.UI.Xaml.Media.MicaBackdrop();
        window.AppWindow.Resize(new Windows.Graphics.SizeInt32(width, height));
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
