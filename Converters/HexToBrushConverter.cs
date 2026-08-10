using System;
using System.Globalization;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Data;
using Microsoft.UI.Xaml.Media;
using Windows.UI;

namespace ChyguiSlide.Converters;

public sealed class HexToBrushConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, string language)
    {
        if (value is string hex && TryParseColor(hex, out var color))
        {
            return new SolidColorBrush(color);
        }

        return new SolidColorBrush(Color.FromArgb(0, 0, 0, 0));
    }

    public object ConvertBack(object value, Type targetType, object parameter, string language) =>
        throw new NotSupportedException();

    private static bool TryParseColor(string hex, out Color color)
    {
        color = Color.FromArgb(0, 0, 0, 0);

        if (string.IsNullOrWhiteSpace(hex))
        {
            return false;
        }

        var cleaned = hex.Trim();
        if (cleaned.StartsWith("#", StringComparison.Ordinal))
        {
            cleaned = cleaned[1..];
        }

        if (cleaned.Length is not (6 or 8))
        {
            return false;
        }

        if (!uint.TryParse(cleaned, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var value))
        {
            return false;
        }

        if (cleaned.Length == 6)
        {
            value |= 0xFF000000;
        }

        var a = (byte)((value & 0xFF000000) >> 24);
        var r = (byte)((value & 0x00FF0000) >> 16);
        var g = (byte)((value & 0x0000FF00) >> 8);
        var b = (byte)(value & 0x000000FF);

        color = Color.FromArgb(a, r, g, b);
        return true;
    }
}

