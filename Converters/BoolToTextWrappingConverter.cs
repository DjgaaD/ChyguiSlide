using Microsoft.UI.Xaml.Data;
using Microsoft.UI.Xaml;

namespace ChyguiSlide.Converters;

public sealed class BoolToTextWrappingConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, string language)
    {
        if (value is bool wordWrap)
        {
            return wordWrap ? TextWrapping.WrapWholeWords : TextWrapping.NoWrap;
        }
        return TextWrapping.WrapWholeWords;
    }

    public object ConvertBack(object value, Type targetType, object parameter, string language) =>
        throw new NotSupportedException();
}







