using ChyguiSlide.Services;
using ChyguiSlide.ViewModels;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace ChyguiSlide.Views;

public sealed partial class AnnouncementEditorPage : Page
{
    public AnnouncementEditorViewModel ViewModel { get; }

    public AnnouncementEditorPage()
    {
        InitializeComponent();
        ViewModel = App.AppHost.Services.GetRequiredService<AnnouncementEditorViewModel>();
        DataContext = ViewModel;
        Loaded += (_, _) => AppUiThemeApplier.ApplyToElement(this);
    }
}
