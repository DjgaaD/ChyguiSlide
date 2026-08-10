using System;
using Microsoft.UI;
using Microsoft.UI.Xaml.Data;
using Microsoft.UI.Xaml.Media;
using Windows.UI;

namespace ChyguiSlide.Converters;

public sealed class BoolToBrushConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, string language)
    {
        if (value is bool isTrue)
        {
            // Зеленый для true, красный для false
            return new SolidColorBrush(isTrue ? Colors.Green : Colors.Red);
        }
        
        return new SolidColorBrush(Colors.Gray);
    }

    public object ConvertBack(object value, Type targetType, object parameter, string language) =>
        throw new NotSupportedException();
}


