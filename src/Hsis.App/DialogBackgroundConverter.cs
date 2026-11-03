using System.Globalization;
using System.Windows;
using System.Windows.Data;
using System.Windows.Media;

namespace Hsis.App;

public sealed class DialogBackgroundConverter : IValueConverter
{
    private static readonly SolidColorBrush DarkBackground = CreateDarkBackground();

    private static SolidColorBrush CreateDarkBackground()
    {
        SolidColorBrush brush = new(Color.FromRgb(43, 43, 43));
        brush.Freeze();
        return brush;
    }

    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (SystemParameters.HighContrast) return SystemColors.WindowBrush;
        // A Fluent window may have a transparent Mica background. Its text colour
        // follows the effective theme, so choose an opaque surface from that colour.
        if (value is SolidColorBrush foreground)
        {
            Color color = foreground.Color;
            double luminance = 0.2126 * color.R + 0.7152 * color.G + 0.0722 * color.B;
            if (luminance > 128) return DarkBackground;
        }
        return Brushes.White;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => Binding.DoNothing;
}
