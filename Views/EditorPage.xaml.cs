using ChyguiSlide.Services;
using ChyguiSlide.ViewModels;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace ChyguiSlide.Views;

public sealed partial class EditorPage : Page
{
    public SongEditorViewModel ViewModel { get; }

    public EditorPage()
    {
        InitializeComponent();
        ViewModel = App.AppHost.Services.GetRequiredService<SongEditorViewModel>();
        DataContext = ViewModel;
        Loaded += OnLoaded;
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        AppUiThemeApplier.ApplyToElement(this);
    }
}
