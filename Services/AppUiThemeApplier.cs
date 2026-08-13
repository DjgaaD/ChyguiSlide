using ChyguiSlide.Services.Models;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Windows.UI.ViewManagement;

namespace ChyguiSlide.Services;

/// <summary>
/// Тема UI через RequestedTheme на окне и диалогах.
/// Application.RequestedTheme после старта менять нельзя — только ElementTheme на FrameworkElement.
/// </summary>
public static class AppUiThemeApplier
{
    private static ElementTheme _requestedTheme = ElementTheme.Default;

    public static ElementTheme ToElementTheme(AppUiThemeMode mode) =>
        mode switch
        {
            AppUiThemeMode.Light => ElementTheme.Light,
            AppUiThemeMode.Dark => ElementTheme.Dark,
            _ => ElementTheme.Default
        };

    public static void Apply(AppUiThemeMode mode)
    {
        _requestedTheme = ToElementTheme(mode);
        ApplyTo(App.MainWindow?.Content as FrameworkElement, _requestedTheme);
    }

    /// <summary>
    /// Явная Light/Dark для диалогов и ThemeResource-резолва.
    /// Для «Как в системе» берём ActualTheme окна или цвет Windows.
    /// </summary>
    public static ElementTheme GetCurrentElementTheme()
    {
        if (_requestedTheme != ElementTheme.Default)
        {
            return _requestedTheme;
        }

        if (App.MainWindow?.Content is FrameworkElement root)
        {
            return root.ActualTheme;
        }

        return ResolveSystemElementTheme();
    }

    public static void ApplyToDialog(ContentDialog? dialog)
    {
        if (dialog is null)
        {
            return;
        }

        var theme = GetCurrentElementTheme();
        dialog.RequestedTheme = theme;

        if (dialog.Content is FrameworkElement content)
        {
            content.RequestedTheme = theme;
        }
    }

    public static void ApplyToElement(FrameworkElement? element)
    {
        if (element is null)
        {
            return;
        }

        var theme = GetCurrentElementTheme();

        void ApplyRoot()
        {
            element.RequestedTheme = theme;
        }

        if (element.DispatcherQueue is not null && !element.DispatcherQueue.HasThreadAccess)
        {
            element.DispatcherQueue.TryEnqueue(ApplyRoot);
            return;
        }

        ApplyRoot();
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

    private static ElementTheme ResolveSystemElementTheme()
    {
        var bg = new UISettings().GetColorValue(UIColorType.Background);
        var luminance = (bg.R + bg.G + bg.B) / 3.0;
        return luminance > 128 ? ElementTheme.Light : ElementTheme.Dark;
    }
}
