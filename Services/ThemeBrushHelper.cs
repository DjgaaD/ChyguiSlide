using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;

namespace ChyguiSlide.Services;

/// <summary>
/// Кисти ThemeDictionaries по текущей теме UI приложения (не по застрявшей теме Application).
/// </summary>
internal static class ThemeBrushHelper
{
    public static Brush? Get(string resourceKey, FrameworkElement? relativeTo = null)
    {
        var theme = AppUiThemeApplier.GetCurrentElementTheme();
        if (theme == ElementTheme.Default)
        {
            theme = relativeTo?.ActualTheme
                    ?? (Application.Current.RequestedTheme == ApplicationTheme.Light
                        ? ElementTheme.Light
                        : ElementTheme.Dark);
        }

        if (TryFindBrush(Application.Current.Resources, resourceKey, theme, out var brush))
        {
            return brush;
        }

        return Application.Current.Resources[resourceKey] as Brush;
    }

    private static bool TryFindBrush(
        ResourceDictionary dictionary,
        string resourceKey,
        ElementTheme theme,
        out Brush? brush)
    {
        brush = null;

        foreach (var themeKey in ThemeDictionaryKeys(theme))
        {
            if (!dictionary.ThemeDictionaries.TryGetValue(themeKey, out var themeObj)
                || themeObj is not ResourceDictionary themeDict)
            {
                continue;
            }

            if (TryGetBrushInDictionary(themeDict, resourceKey, out brush))
            {
                return true;
            }
        }

        foreach (var merged in dictionary.MergedDictionaries)
        {
            if (TryFindBrush(merged, resourceKey, theme, out brush))
            {
                return true;
            }
        }

        return false;
    }

    private static bool TryGetBrushInDictionary(ResourceDictionary dictionary, string resourceKey, out Brush? brush)
    {
        if (dictionary.TryGetValue(resourceKey, out var value) && value is Brush found)
        {
            brush = found;
            return true;
        }

        foreach (var merged in dictionary.MergedDictionaries)
        {
            if (TryGetBrushInDictionary(merged, resourceKey, out brush))
            {
                return true;
            }
        }

        brush = null;
        return false;
    }

    private static IEnumerable<string> ThemeDictionaryKeys(ElementTheme theme) =>
        theme switch
        {
            ElementTheme.Light => new[] { "Light" },
            ElementTheme.Dark => new[] { "Default", "Dark" },
            _ => Application.Current.RequestedTheme == ApplicationTheme.Light
                ? new[] { "Light", "Default" }
                : new[] { "Default", "Dark", "Light" }
        };
}
