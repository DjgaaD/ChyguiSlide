using Microsoft.UI.Xaml.Data;
using Microsoft.UI.Xaml.Media;

namespace ChyguiSlide.Converters;

public sealed class BoolToStretchConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, string language)
    {
        if (value is bool wordWrap)
        {
            // При включенном переносе используем Uniform для сохранения пропорций
            // Текст уже разбит на строки программно, поэтому Uniform даст максимальный размер
            // При выключенном переносе также используем Uniform
            return Stretch.Uniform;
        }
        return Stretch.Uniform;
    }

    public object ConvertBack(object value, Type targetType, object parameter, string language) =>
        throw new NotSupportedException();
}







