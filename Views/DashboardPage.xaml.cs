using ChyguiSlide.ViewModels;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace ChyguiSlide.Views;

public sealed partial class DashboardPage : Page
{
    public DashboardViewModel ViewModel { get; }
    private static bool _initializedOnce;

    public DashboardPage()
    {
        InitializeComponent();
        ViewModel = App.AppHost.Services.GetRequiredService<DashboardViewModel>();
        DataContext = ViewModel;
    }

    private async void OnPageLoaded(object sender, RoutedEventArgs e)
    {
        // Инициализируем только один раз при первом запуске
        if (!_initializedOnce)
        {
            _initializedOnce = true;
            await ViewModel.InitializeAsync();
        }
    }
}
