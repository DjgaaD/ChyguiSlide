using System;
using System.ComponentModel;
using ChyguiSlide.Data.Entities;
using ChyguiSlide.ViewModels;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

using ChyguiSlide.Services;
using ChyguiSlide.Services.Abstractions;
using ChyguiSlide.Views.UiAnimation;

namespace ChyguiSlide.Views;

public sealed partial class LiveControlPage : Page
{
    public LiveControlViewModel ViewModel { get; }

    private IProjectionDisplayService? _projectionDisplayService;
    private ListSelectionStripeBinder? _sectionStripe;

    public LiveControlPage()
    {
        InitializeComponent();
        ChyguiSlide.Data.InteractionLogger.Log("LiveControlPage ctor");
        ViewModel = App.AppHost.Services.GetRequiredService<LiveControlViewModel>();
        DataContext = ViewModel;
        ViewModel.PropertyChanged += OnViewModelPropertyChanged;
        Loaded += OnLoaded;
    }

    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        await ApplyModernLayoutAsync();

        // NavigationCacheMode=Required: страница переживает уход с раздела.
        // Не дергаем RefreshQueue при каждом появлении — ListView иначе заново
        // выставляет SelectedItem → ShowSongSections → пересборка слайда → мерцание видеофона.
        await ViewModel.InitializeAsync();
        ScrollCurrentSectionIntoView();

        InitializeProjectionMirror();
    }

    protected override void OnNavigatedTo(Microsoft.UI.Xaml.Navigation.NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);
        _ = ApplyLayoutAndScrollAsync();
    }

    private async Task ApplyLayoutAndScrollAsync()
    {
        await ApplyModernLayoutAsync();
        InitializeProjectionMirror();
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

        if (e.PropertyName == nameof(LiveControlViewModel.SelectedSection))
        {
            _sectionStripe?.RequestUpdate(animate: true);
        }
    }

    private void OnQuickPlaylistDragItemsCompleted(ListViewBase sender, DragItemsCompletedEventArgs args)
    {
        ViewModel.SyncQuickPlaylistOrderFromUi();
    }

    private void OnRemoveQuickEntryClick(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: PlaylistEntry entry })
        {
            return;
        }

        ViewModel.RemoveQuickEntry(entry);
    }

    private void ScrollCurrentSectionIntoView()
    {
        var section = ViewModel.SelectedSection;
        var sectionList = GetActiveSectionList();
        if (section is null || sectionList is null)
        {
            return;
        }

        sectionList.DispatcherQueue.TryEnqueue(DispatcherQueuePriority.Low, () =>
        {
            var current = ViewModel.SelectedSection;
            if (current is null)
            {
                return;
            }

            _sectionStripe?.ScrollSelectedIntoViewIfNeeded();
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
        ChyguiSlide.Data.InteractionLogger.Log("InitializeProjectionMirror: enter");
        _projectionDisplayService = App.AppHost.Services.GetService<IProjectionDisplayService>();
        if (_projectionDisplayService is null)
        {
            ChyguiSlide.Data.InteractionLogger.Log("InitializeProjectionMirror: projectionDisplayService is null");
            return;
        }

        // Используем существующий механизм сервиса для привязки превью
        // Сервис показывает тот же HTML/CSS, что и окно проектора.
        var previewHost = GetActivePreviewStageHost();
        ChyguiSlide.Data.InteractionLogger.Log($"InitializeProjectionMirror: PreviewStageHost is {(previewHost is null ? "null" : "present")}");
        if (previewHost is not null)
        {
            _projectionDisplayService.BindProgramPreviewHost(previewHost);
        }

        _projectionDisplayService.ProjectionWindowVisibilityChanged -= OnProjectionWindowVisibilityChanged;
        _projectionDisplayService.ProjectionWindowVisibilityChanged += OnProjectionWindowVisibilityChanged;
        OnProjectionWindowVisibilityChanged(_projectionDisplayService, _projectionDisplayService.IsOpen);
    }

    private void OnProjectionWindowVisibilityChanged(object? sender, bool isVisible)
    {
        // Показываем превью только когда трансляция запущена
        var previewHost = GetActivePreviewStageHost();
        var idleHint = GetActivePreviewIdleHint();
        if (previewHost is null || idleHint is null)
        {
            return;
        }

        if (isVisible)
        {
            previewHost.Visibility = Visibility.Visible;
            idleHint.Visibility = Visibility.Collapsed;
            
            // Принудительно обновляем превью при открытии трансляции
            _projectionDisplayService?.EnsureContentVisible();
        }
        else
        {
            previewHost.Visibility = Visibility.Collapsed;
            idleHint.Visibility = Visibility.Visible;
        }
    }

    private async Task ApplyModernLayoutAsync()
    {
        await ApplyPreviewCanvasAsync();
        EnsureModernSectionStripe();
    }

    private void EnsureModernSectionStripe()
    {
        if (SectionListModern is null || ModernSectionStripe is null || ModernSectionHost is null)
        {
            return;
        }

        _sectionStripe ??= new ListSelectionStripeBinder(
            SectionListModern,
            ModernSectionStripe,
            ModernSectionHost,
            () => ViewModel.SelectedSection,
            DispatcherQueue);
        _sectionStripe.Attach();
        _sectionStripe.RequestUpdate(animate: false);
    }

    private async Task ApplyPreviewCanvasAsync()
    {
        var (width, height) = await ProjectionOutputSize.GetAsync();
        if (PreviewCanvasModern is not null)
        {
            ProjectionOutputSize.ApplyCanvas(PreviewCanvasModern, width, height);
        }

        if (_projectionDisplayService?.ProgramStage is ChyguiSlide.Controls.WebProjectionPreview preview)
        {
            preview.ApplyOutputSize(width, height);
        }
    }

    private ListView? GetActiveSectionList() => SectionListModern;

    private Grid? GetActivePreviewStageHost() => PreviewStageHostModern;

    private TextBlock? GetActivePreviewIdleHint() => PreviewIdleHintModern;

    private async void OnOpenProjectionClick(object sender, RoutedEventArgs e)
    {
        ChyguiSlide.Data.InteractionLogger.Log("OnOpenProjectionClick: button clicked");

        try
        {
            if (ViewModel.OpenProjectionCommand is not null && ViewModel.OpenProjectionCommand.CanExecute(null))
            {
                ChyguiSlide.Data.InteractionLogger.Log("OnOpenProjectionClick: executing OpenProjectionCommand");
                await ViewModel.OpenProjectionCommand.ExecuteAsync(null);
                ChyguiSlide.Data.InteractionLogger.Log("OnOpenProjectionClick: command executed");
            }
            else
            {
                ChyguiSlide.Data.InteractionLogger.Log("OnOpenProjectionClick: command cannot execute or is null");
            }
        }
        catch (Exception ex)
        {
            ChyguiSlide.Data.InteractionLogger.Log($"OnOpenProjectionClick: exception {ex.Message}");
        }
    }
}
