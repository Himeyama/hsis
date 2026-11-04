using System.Windows;
using System.Windows.Controls;

namespace Hsis.App;

public partial class TitleBar : UserControl
{
    private Window? _window;

    public TitleBar() => InitializeComponent();

    private void TitleBar_Loaded(object sender, RoutedEventArgs e)
    {
        _window = Window.GetWindow(this);
        if (_window is null) return;
        _window.StateChanged += Window_StateChanged;
        UpdateMaximizeIcon();
    }

    private void TitleBar_Unloaded(object sender, RoutedEventArgs e)
    {
        if (_window is not null) _window.StateChanged -= Window_StateChanged;
        _window = null;
    }

    private void Window_StateChanged(object? sender, EventArgs e) => UpdateMaximizeIcon();
    private void UpdateMaximizeIcon() => MaximizeButton.Content = _window?.WindowState == WindowState.Maximized ? "M 0,3 L 7,3 7,10 0,10 Z M 3,3 L 3,0 10,0 10,7 7,7" : "M 0,0 L 10,0 10,10 0,10 Z";
    private void Minimize_Click(object sender, RoutedEventArgs e)
    {
        if (_window is not null) SystemCommands.MinimizeWindow(_window);
    }
    private void Maximize_Click(object sender, RoutedEventArgs e)
    {
        if (_window is null) return;
        if (_window.WindowState == WindowState.Maximized) SystemCommands.RestoreWindow(_window);
        else SystemCommands.MaximizeWindow(_window);
    }
    private void Close_Click(object sender, RoutedEventArgs e)
    {
        if (_window is not null) SystemCommands.CloseWindow(_window);
    }
}
