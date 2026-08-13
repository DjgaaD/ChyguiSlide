using Microsoft.UI.Xaml.Controls;

namespace ChyguiSlide.Services;

internal static class ContentDialogTheme
{
    public static async Task<ContentDialogResult> ShowAsync(ContentDialog dialog)
    {
        AppUiThemeApplier.ApplyToDialog(dialog);
        return await dialog.ShowAsync();
    }
}
