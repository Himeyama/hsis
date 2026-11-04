using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media.Imaging;
using System.Windows.Media;
using System.Runtime.InteropServices;
using System.Windows.Shell;
using Microsoft.Win32;

namespace Hsis.App;

internal static class WindowTools
{
    private static bool _fontHandlerRegistered;
    private static FontFamily? _wineFont;

    private static FontFamily? FindWineFont()
    {
        string fontDirectory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "Fonts");
        string regular = Path.Combine(fontDirectory, "NotoSansJP-Regular.otf");
        string bold = Path.Combine(fontDirectory, "NotoSansJP-Bold.otf");
        if (File.Exists(regular))
        {
            string isolatedDirectory = Path.Combine(Path.GetTempPath(), "Hsis", "Fonts", Guid.NewGuid().ToString("N"));
            try
            {
                Directory.CreateDirectory(isolatedDirectory);
                File.Copy(regular, Path.Combine(isolatedDirectory, Path.GetFileName(regular)));
                if (File.Exists(bold)) File.Copy(bold, Path.Combine(isolatedDirectory, Path.GetFileName(bold)));
                Uri location = new(isolatedDirectory + Path.DirectorySeparatorChar, UriKind.Absolute);
                FontFamily? staticFont = Fonts.GetFontFamilies(location).FirstOrDefault(family =>
                    family.FamilyNames.Values.Any(name => name.Contains("Noto Sans", StringComparison.OrdinalIgnoreCase)));
                AppDomain.CurrentDomain.ProcessExit += (_, _) =>
                {
                    try { Directory.Delete(isolatedDirectory, true); }
                    catch (IOException) { }
                    catch (UnauthorizedAccessException) { }
                };
                if (staticFont is not null) return staticFont;
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or NotSupportedException or ArgumentException)
            {
                // Fall back to the installed font if the isolated copy cannot be loaded.
            }
        }
        return Fonts.SystemFontFamilies.FirstOrDefault(family =>
            string.Equals(family.Source, "Noto Sans JP", StringComparison.OrdinalIgnoreCase) ||
            family.FamilyNames.Values.Any(name => string.Equals(name, "Noto Sans JP", StringComparison.OrdinalIgnoreCase)));
    }

    private static void ApplyWineFont(Window window)
    {
        if (!NativeLibrary.TryLoad("ntdll.dll", out IntPtr library)) return;
        try
        {
            if (!NativeLibrary.TryGetExport(library, "wine_get_version", out _)) return;
            FontFamily? font = _wineFont ??= FindWineFont();
            if (font is null) return;
            window.FontFamily = font;
            if (_fontHandlerRegistered) return;
            _fontHandlerRegistered = true;
            EventManager.RegisterClassHandler(typeof(FrameworkElement), FrameworkElement.LoadedEvent,
                new RoutedEventHandler((sender, _) =>
                {
                    if (sender is Control control && control.ReadLocalValue(Control.FontFamilyProperty) == DependencyProperty.UnsetValue)
                        control.SetCurrentValue(Control.FontFamilyProperty, font);
                    if (sender is ButtonBase button && button.FontWeight < FontWeights.Medium)
                        button.SetCurrentValue(Control.FontWeightProperty, FontWeights.Medium);
                    if (sender is TextBlock text)
                    {
                        bool insideButton = false;
                        for (DependencyObject? parent = VisualTreeHelper.GetParent(text); parent is not null; parent = VisualTreeHelper.GetParent(parent))
                        {
                            if (parent is ButtonBase) { insideButton = true; break; }
                        }
                        if (insideButton || text.ReadLocalValue(TextBlock.FontFamilyProperty) == DependencyProperty.UnsetValue)
                            text.SetCurrentValue(TextBlock.FontFamilyProperty, font);
                        if (insideButton && text.FontWeight < FontWeights.Medium)
                            text.SetCurrentValue(TextBlock.FontWeightProperty, FontWeights.Medium);
                    }
                }));
        }
        finally { NativeLibrary.Free(library); }
    }

    public static void Configure(Window window)
    {
        ApplyWineFont(window);
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
