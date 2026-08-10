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
        // Не вызываем InitializeAsync/LoadSongs на Loaded:
        // OpenEditorDialogAsync уже готовит VM (новая / выбранная / импорт).
        // Поздний LoadSongs раньше подставлял Songs.First() поверх CreateNewSong.
    }
}
