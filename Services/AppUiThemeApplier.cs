using ChyguiSlide.Services.Models;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

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

    /// <summary>
    /// Тема для ContentDialog и всплывающих окон: они не наследуют RequestedTheme от MainWindow.Content.
    /// </summary>
    public static ElementTheme GetCurrentElementTheme()
    {
        if (App.MainWindow?.Content is FrameworkElement root)
        {
            return root.RequestedTheme == ElementTheme.Default
                ? root.ActualTheme
                : root.RequestedTheme;
        }

        return ElementTheme.Default;
    }

    public static void ApplyToDialog(ContentDialog? dialog)
    {
        if (dialog is null)
        {
            return;
        }

        dialog.RequestedTheme = GetCurrentElementTheme();
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
