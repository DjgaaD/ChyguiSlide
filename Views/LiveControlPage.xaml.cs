using System;
using System.ComponentModel;
using ChyguiSlide.Data.Entities;
using ChyguiSlide.ViewModels;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml;

using ChyguiSlide.Services;
using ChyguiSlide.Services.Abstractions;

namespace ChyguiSlide.Views;

public sealed partial class LiveControlPage : Page
{
    public LiveControlViewModel ViewModel { get; }
    
    private IProjectionDisplayService? _projectionDisplayService;

    public LiveControlPage()
    {
        InitializeComponent();
        ViewModel = App.AppHost.Services.GetRequiredService<LiveControlViewModel>();
        DataContext = ViewModel;
        ViewModel.PropertyChanged += OnViewModelPropertyChanged;
        Loaded += OnLoaded;
    }

    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        // NavigationCacheMode=Required: страница переживает уход с раздела.
        // Не дергаем RefreshQueue при каждом появлении — ListView иначе заново
        // выставляет SelectedItem → ShowSongSections → пересборка слайда → мерцание видеофона.
        await ViewModel.InitializeAsync();
        ScrollCurrentSectionIntoView();
        
        // Инициализация зеркалирования экрана проекции
        InitializeProjectionMirror();
    }

    protected override void OnNavigatedTo(Microsoft.UI.Xaml.Navigation.NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);
        ScrollCurrentSectionIntoView();
    }

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(LiveControlViewModel.SelectedSection)
            or nameof(LiveControlViewModel.SectionProgressLabel)
            or nameof(LiveControlViewModel.CurrentState))
        {
            ScrollCurrentSectionIntoView();
        }
    }

    private void OnQuickPlaylistDragItemsCompleted(ListViewBase sender, DragItemsCompletedEventArgs args)
    {
        ViewModel.SyncQuickPlaylistOrderFromUi();
    }

    private void ScrollCurrentSectionIntoView()
    {
        var section = ViewModel.SelectedSection;
        if (section is null || SectionList is null)
        {
            return;
        }

        SectionList.DispatcherQueue.TryEnqueue(DispatcherQueuePriority.Low, () =>
        {
            var current = ViewModel.SelectedSection;
            if (current is null)
            {
                return;
            }

            SectionList.ScrollIntoView(current, ScrollIntoViewAlignment.Default);
        });
    }

    private async void OnSavedPlaylistItemClick(object sender, ItemClickEventArgs e)
    {
        if (e.ClickedItem is not Playlist playlist)
        {
            return;
        }

        // Клик по кнопке удаления не должен загружать плейлист
        if (e.OriginalSource is DependencyObject source
            && FindAncestor<Button>(source) is not null)
        {
            return;
        }

        // Проверяем, что XamlRoot доступен
        if (XamlRoot is null)
        {
            ViewModel.LoadSavedPlaylistCommand.Execute(playlist);
            return;
        }

        // Показываем диалог подтверждения
        var dialog = new ContentDialog
        {
            Title = "Замена быстрого плейлиста",
            Content = "Вы точно хотите заменить быстрый плейлист?",
            PrimaryButtonText = "Да",
            SecondaryButtonText = "Отмена",
            XamlRoot = XamlRoot,
            DefaultButton = ContentDialogButton.Secondary
        };

        var result = await ContentDialogTheme.ShowAsync(dialog);
        if (result == ContentDialogResult.Primary)
        {
            ViewModel.LoadSavedPlaylistCommand.Execute(playlist);
        }
    }

    private async void OnDeleteSavedPlaylistClick(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: Playlist playlist })
        {
            return;
        }

        var dialog = new ContentDialog
        {
            Title = "Удаление плейлиста",
            Content = $"Удалить плейлист «{playlist.Name}»?",
            PrimaryButtonText = "Удалить",
            CloseButtonText = "Отмена",
            DefaultButton = ContentDialogButton.Close,
            XamlRoot = XamlRoot
        };

        if (await ContentDialogTheme.ShowAsync(dialog) == ContentDialogResult.Primary)
        {
            await ViewModel.DeleteSavedPlaylistCommand.ExecuteAsync(playlist);
        }
    }

    private async void OnClearQuickPlaylistClick(object sender, RoutedEventArgs e)
    {
        if (ViewModel.QuickEntries.Count == 0)
        {
            return;
        }

        var dialog = new ContentDialog
        {
            Title = "Сброс быстрого плейлиста",
            Content = "Очистить быстрый плейлист?",
            PrimaryButtonText = "Сбросить",
            CloseButtonText = "Отмена",
            DefaultButton = ContentDialogButton.Close,
            XamlRoot = XamlRoot
        };

        if (await ContentDialogTheme.ShowAsync(dialog) == ContentDialogResult.Primary)
        {
            ViewModel.ClearQuickPlaylistCommand.Execute(null);
        }
    }

    private async void OnSaveQuickPlaylistClick(object sender, RoutedEventArgs e)
    {
        if (ViewModel.QuickEntries.Count == 0)
        {
            return;
        }

        var textBox = new TextBox
        {
            Header = "Название плейлиста",
            PlaceholderText = "Введите название",
            Width = 300
        };

        var dialog = new ContentDialog
        {
            Title = "Сохранить быстрый плейлист",
            Content = textBox,
            PrimaryButtonText = "Сохранить",
            SecondaryButtonText = "Отмена",
            XamlRoot = XamlRoot,
            DefaultButton = ContentDialogButton.Primary
        };

        textBox.Loaded += (s, args) => textBox.Focus(FocusState.Programmatic);

        var result = await ContentDialogTheme.ShowAsync(dialog);
        if (result == ContentDialogResult.Primary)
        {
            var playlistName = textBox.Text?.Trim();
            if (!string.IsNullOrWhiteSpace(playlistName))
            {
                await ViewModel.SaveQuickPlaylistCommand.ExecuteAsync(playlistName);
            }
        }
    }

    private static T? FindAncestor<T>(DependencyObject start) where T : DependencyObject
    {
        var current = start;
        while (current is not null)
        {
            if (current is T match)
            {
                return match;
            }

            current = Microsoft.UI.Xaml.Media.VisualTreeHelper.GetParent(current);
        }

        return null;
    }

    private void InitializeProjectionMirror()
    {
        _projectionDisplayService = App.AppHost.Services.GetService<IProjectionDisplayService>();
        if (_projectionDisplayService is null)
        {
            return;
        }

        // Используем существующий механизм сервиса для привязки превью
        // Сервис автоматически управляет перемещением ProjectionStageView между окном и превью
        _projectionDisplayService.BindProgramPreviewHost(PreviewStageHost);

        // Подписываемся на изменения видимости окна проекции
        _projectionDisplayService.ProjectionWindowVisibilityChanged += OnProjectionWindowVisibilityChanged;
    }

    private void OnProjectionWindowVisibilityChanged(object? sender, bool isVisible)
    {
        // Показываем превью только когда трансляция запущена
        if (isVisible)
        {
            PreviewStageHost.Visibility = Visibility.Visible;
            PreviewIdleHint.Visibility = Visibility.Collapsed;
            
            // Принудительно обновляем превью при открытии трансляции
            _projectionDisplayService?.EnsureContentVisible();
        }
        else
        {
            PreviewStageHost.Visibility = Visibility.Collapsed;
            PreviewIdleHint.Visibility = Visibility.Visible;
        }
    }
}
