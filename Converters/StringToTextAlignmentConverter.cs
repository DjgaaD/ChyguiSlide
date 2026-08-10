using Microsoft.UI.Xaml.Data;
using Microsoft.UI.Xaml;

namespace ChyguiSlide.Converters;

public sealed class StringToTextAlignmentConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, string language)
    {
        if (value is string alignment)
        {
            return alignment switch
            {
                "Left" => TextAlignment.Left,
                "Center" => TextAlignment.Center,
                "Right" => TextAlignment.Right,
                "Justify" => TextAlignment.Justify,
                _ => TextAlignment.Center
            };
        }

        return TextAlignment.Center;
    }

    public object ConvertBack(object value, Type targetType, object parameter, string language)
    {
        if (value is TextAlignment alignment)
        {
            return alignment switch
            {
                TextAlignment.Left => "Left",
                TextAlignment.Center => "Center",
                TextAlignment.Right => "Right",
                TextAlignment.Justify => "Justify",
                _ => "Center"
            };
        }

        return "Center";
    }
}







