using System;
using Microsoft.UI.Xaml.Data;

namespace ChyguiSlide.Converters;

/// <summary>true → приглушённая непрозрачность (уже проигранные пункты плейлиста).</summary>
public sealed class BoolToDimOpacityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, string language) =>
        value is true ? 0.42 : 1.0;

    public object ConvertBack(object value, Type targetType, object parameter, string language) =>
        throw new NotSupportedException();
}
