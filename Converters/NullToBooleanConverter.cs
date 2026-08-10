using System;
using Microsoft.UI.Xaml.Data;

namespace ChyguiSlide.Converters;

public sealed class NullToBooleanConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, string language)
    {
        return value is not null;
    }

    public object ConvertBack(object value, Type targetType, object parameter, string language)
    {
        throw new NotSupportedException();
    }
}









