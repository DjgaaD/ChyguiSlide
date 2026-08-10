using ChyguiSlide.ViewModels;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace ChyguiSlide.Views;

public sealed partial class AnnouncementsPage : Page
{
    public AnnouncementsViewModel ViewModel { get; }

    public AnnouncementsPage()
    {
        InitializeComponent();
        ViewModel = App.AppHost.Services.GetRequiredService<AnnouncementsViewModel>();
        DataContext = ViewModel;
    }

    private async void OnPageLoaded(object sender, RoutedEventArgs e)
    {
        await ViewModel.InitializeAsync();
    }
}
