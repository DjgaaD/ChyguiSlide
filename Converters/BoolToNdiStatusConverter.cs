using System;
using Microsoft.UI.Xaml.Data;

namespace ChyguiSlide.Converters;

public sealed class BoolToNdiStatusConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, string language)
    {
        if (value is bool isConnected)
        {
            return isConnected ? "Подключено" : "Отключено";
        }
        
        return "Неизвестно";
    }

    public object ConvertBack(object value, Type targetType, object parameter, string language) =>
        throw new NotSupportedException();
}


