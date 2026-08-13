using ChyguiSlide.Services;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Data;
using Microsoft.UI.Xaml.Media;

namespace ChyguiSlide.Converters;

public sealed class BoolToAccentBorderBrushConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, string language)
    {
        var selected = value is true;
        if (selected)
        {
            return ThemeBrushHelper.Get("AccentFillColorDefaultBrush")
                   ?? ThemeBrushHelper.Get("AccentFillColorPrimaryBrush")
                   ?? new SolidColorBrush(Microsoft.UI.Colors.DodgerBlue);
        }

        return ThemeBrushHelper.Get("CardStrokeColorDefaultBrush")
               ?? new SolidColorBrush(Microsoft.UI.Colors.Gray);
    }

    public object ConvertBack(object value, Type targetType, object parameter, string language) =>
        throw new NotSupportedException();
}

public sealed class BoolToAccentBorderThicknessConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, string language)
    {
        return value is true ? new Thickness(3) : new Thickness(1);
    }

    public object ConvertBack(object value, Type targetType, object parameter, string language) =>
        throw new NotSupportedException();
}

/// <summary>Фон карточки текущей секции — лёгкий акцент вместо стандартного selection-rect.</summary>
public sealed class BoolToAccentCardBackgroundConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, string language)
    {
        if (value is true)
        {
            return ThemeBrushHelper.Get("AccentFillColorTertiaryBrush")
                   ?? ThemeBrushHelper.Get("SubtleFillColorSecondaryBrush")
                   ?? new SolidColorBrush(Microsoft.UI.Colors.Transparent);
        }

        return ThemeBrushHelper.Get("CardBackgroundFillColorDefaultBrush")
               ?? ThemeBrushHelper.Get("LayerFillColorDefaultBrush")
               ?? new SolidColorBrush(Microsoft.UI.Colors.Transparent);
    }

    public object ConvertBack(object value, Type targetType, object parameter, string language) =>
        throw new NotSupportedException();
}

/// <summary>Цвет текста заголовка текущей секции.</summary>
public sealed class BoolToAccentTitleBrushConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, string language)
    {
        if (value is true)
        {
            return ThemeBrushHelper.Get("AccentTextFillColorPrimaryBrush")
                   ?? ThemeBrushHelper.Get("AccentFillColorDefaultBrush")
                   ?? new SolidColorBrush(Microsoft.UI.Colors.DodgerBlue);
        }

        return ThemeBrushHelper.Get("TextFillColorPrimaryBrush")
               ?? new SolidColorBrush(Microsoft.UI.Colors.Gray);
    }

    public object ConvertBack(object value, Type targetType, object parameter, string language) =>
        throw new NotSupportedException();
}
