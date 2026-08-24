using ChyguiSlide.Services.Abstractions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;

namespace ChyguiSlide.Views;

internal static class ProjectionOutputSize
{
    public static async Task<(int Width, int Height)> GetAsync()
    {
        try
        {
            var settings = App.AppHost.Services.GetRequiredService<IDisplaySettingsService>();
            var display = await settings.GetSelectedDisplayAsync();
            var width = display?.Width ?? 1920;
            var height = display?.Height ?? 1080;
            if (width < 800 || height < 600)
            {
                return (1920, 1080);
            }

            return (width, height);
        }
        catch
        {
            return (1920, 1080);
        }
    }

    public static void ApplyCanvas(FrameworkElement canvas, int width, int height)
    {
        canvas.Width = width;
        canvas.Height = height;
    }
}
