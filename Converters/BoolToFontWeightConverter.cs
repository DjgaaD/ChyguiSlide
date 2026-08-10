using Microsoft.UI.Text;
using Microsoft.UI.Xaml.Data;
using Windows.UI.Text;

namespace ChyguiSlide.Converters;

public sealed class BoolToFontWeightConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, string language)
        => value is true ? FontWeights.Bold : FontWeights.Normal;

    public object ConvertBack(object value, Type targetType, object parameter, string language)
        => value is FontWeight weight && weight.Weight >= FontWeights.SemiBold.Weight;
}
