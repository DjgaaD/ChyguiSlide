using ChyguiSlide.ViewModels;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace ChyguiSlide.Views;

public sealed partial class DashboardPage : Page
{
    public DashboardViewModel ViewModel { get; }

    public DashboardPage()
    {
        InitializeComponent();
        ViewModel = App.AppHost.Services.GetRequiredService<DashboardViewModel>();
        DataContext = ViewModel;
    }

    private async void OnPageLoaded(object sender, RoutedEventArgs e)
    {
        await ViewModel.InitializeAsync();
    }
}
