using System;
using Microsoft.UI.Xaml.Data;

namespace ChyguiSlide.Converters;

public sealed class BoolToOpacityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, string language)
    {
        return value is bool b && b ? 1.0 : 0.0;
    }

    public object ConvertBack(object value, Type targetType, object parameter, string language) =>
        throw new NotSupportedException();
}











