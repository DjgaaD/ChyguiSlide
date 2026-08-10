using Microsoft.UI.Xaml.Data;
using Microsoft.UI.Xaml;

namespace ChyguiSlide.Converters;

public sealed class TextAlignmentToBorderThicknessConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, string language)
    {
        if (value is string currentAlignment && parameter is string buttonAlignment)
        {
            if (currentAlignment == buttonAlignment)
            {
                // Выбранная кнопка - возвращаем толщину рамки
                return new Thickness(2);
            }
        }

        return new Thickness(0);
    }

    public object ConvertBack(object value, Type targetType, object parameter, string language) =>
        throw new NotSupportedException();
}







