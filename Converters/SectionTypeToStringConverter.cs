using System;
using ChyguiSlide.Data.Enums;
using Microsoft.UI.Xaml.Data;

namespace ChyguiSlide.Converters;

public sealed class SectionTypeToStringConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, string language)
    {
        if (value is SectionType sectionType)
        {
            return sectionType switch
            {
                SectionType.Verse => "Куплет",
                SectionType.Chorus => "Припев",
                _ => "Секция"
            };
        }

        return value?.ToString() ?? string.Empty;
    }

    public object ConvertBack(object value, Type targetType, object parameter, string language) =>
        throw new NotSupportedException();
}





