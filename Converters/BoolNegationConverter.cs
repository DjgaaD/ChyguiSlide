using System;
using Microsoft.UI.Xaml.Data;

namespace ChyguiSlide.Converters;

public sealed class BoolNegationConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, string language)
    {
        return value is bool b ? !b : false;
    }

    public object ConvertBack(object value, Type targetType, object parameter, string language)
    {
        return value is bool b ? !b : false;
    }
}











