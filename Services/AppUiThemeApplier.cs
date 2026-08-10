using ChyguiSlide.Services.Models;
using Microsoft.UI.Xaml;

namespace ChyguiSlide.Services;

public static class AppUiThemeApplier
{
    public static ElementTheme ToElementTheme(AppUiThemeMode mode) =>
        mode switch
        {
            AppUiThemeMode.Light => ElementTheme.Light,
            AppUiThemeMode.Dark => ElementTheme.Dark,
            _ => ElementTheme.Default
        };

    public static void Apply(AppUiThemeMode mode)
    {
        ApplyTo(App.MainWindow?.Content as FrameworkElement, ToElementTheme(mode));
    }

    private static void ApplyTo(FrameworkElement? root, ElementTheme theme)
    {
        if (root is null)
        {
            return;
        }

        if (root.DispatcherQueue is not null && !root.DispatcherQueue.HasThreadAccess)
        {
            root.DispatcherQueue.TryEnqueue(() => root.RequestedTheme = theme);
            return;
        }

        root.RequestedTheme = theme;
    }
}
