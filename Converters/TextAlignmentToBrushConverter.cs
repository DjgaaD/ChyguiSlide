using Microsoft.UI.Xaml.Data;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI;
using Windows.UI;

namespace ChyguiSlide.Converters;

public sealed class TextAlignmentToBrushConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, string language)
    {
        if (value is string currentAlignment && parameter is string buttonAlignment)
        {
            if (currentAlignment == buttonAlignment)
            {
                // Выбранная кнопка - возвращаем цвет фона и рамки
                return new SolidColorBrush(Color.FromArgb(30, 81, 43, 212)); // Accent color с прозрачностью
            }
        }

        return new SolidColorBrush(Colors.Transparent);
    }

    public object ConvertBack(object value, Type targetType, object parameter, string language) =>
        throw new NotSupportedException();
}







