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
            return Application.Current.Resources["AccentFillColorDefaultBrush"] as Brush
                   ?? Application.Current.Resources["AccentFillColorPrimaryBrush"] as Brush
                   ?? new SolidColorBrush(Microsoft.UI.Colors.DodgerBlue);
        }

        return Application.Current.Resources["CardStrokeColorDefaultBrush"] as Brush
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
            return Application.Current.Resources["AccentFillColorTertiaryBrush"] as Brush
                   ?? Application.Current.Resources["SubtleFillColorSecondaryBrush"] as Brush
                   ?? new SolidColorBrush(Microsoft.UI.Colors.Transparent);
        }

        return Application.Current.Resources["CardBackgroundFillColorDefaultBrush"] as Brush
               ?? Application.Current.Resources["LayerFillColorDefaultBrush"] as Brush
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
            return Application.Current.Resources["AccentTextFillColorPrimaryBrush"] as Brush
                   ?? Application.Current.Resources["AccentFillColorDefaultBrush"] as Brush
                   ?? new SolidColorBrush(Microsoft.UI.Colors.DodgerBlue);
        }

        return Application.Current.Resources["TextFillColorPrimaryBrush"] as Brush
               ?? new SolidColorBrush(Microsoft.UI.Colors.Black);
    }

    public object ConvertBack(object value, Type targetType, object parameter, string language) =>
        throw new NotSupportedException();
}
