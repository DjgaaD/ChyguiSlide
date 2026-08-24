using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ChyguiSlide.Data.Entities;
using ChyguiSlide.Data.Enums;
using ChyguiSlide.Services.Abstractions;
using ChyguiSlide.Services.Models;
using Microsoft.UI.Dispatching;
using NdiSource = ChyguiSlide.Services.Abstractions.NdiSource;
using ChyguiSlide.Views.Dialogs;
using ChyguiSlide.Data;

namespace ChyguiSlide.ViewModels;

public sealed partial class LiveControlViewModel : ObservableRecipient
{
    private readonly ICatalogService _catalogService;
    private readonly IProjectionStateService _projectionStateService;
    private readonly IProjectionDisplayService _projectionDisplayService;
    private readonly IDisplaySettingsService _displaySettingsService;
    private readonly ICameraStreamService _cameraStreamService;
    private readonly IBibleService _bibleService;
    private readonly INdiReceiverService? _ndiReceiverService;
    private readonly DispatcherQueue _dispatcher;
    private readonly List<PlaylistEntry> _currentEntries = new();
    private readonly List<SectionSnapshot> _currentSections = new();
    private bool _suppressSectionSelection;
    private DateTime? _lastUserSelectionUtc;
    private bool _isInitialized;
    public bool IsInitialized => _isInitialized;
    private bool _startShowInProgress;
    private string _quickPlaylistName = "Быстрый плейлист";
    private ThemePreset? _currentThemePreset;
    private bool _suppressQuickEntryShow;
    private bool _isUpdatingSectionsHighlight;

    public ObservableCollection<LiveQueueEntry> Queue { get; } = new();
    public ObservableCollection<Playlist> SavedPlaylists { get; } = new();
    public ObservableCollection<PlaylistEntry> QuickEntries { get; } = new();
    public ObservableCollection<string> VisibleLines { get; } = new();
    public ObservableCollection<LiveSectionItem> Sections { get; } = new();
    public ObservableCollection<NdiSource> AvailableNdiSources { get; } = new();

    [ObservableProperty]
    private LiveQueueEntry? selectedEntry;

    [ObservableProperty]
    private PlaylistEntry? selectedQuickEntry;

    partial void OnSelectedQuickEntryChanged(PlaylistEntry? value)
    {
        // При изменении выбора в быстром плейлисте показываем секции
        if (_suppressQuickEntryShow)
        {
            return;
        }

        if (value?.Song is null)
        {
            return;
        }

        // ListView при возврате на страницу снова выставляет SelectedItem —
        // не трогаем проектор, если эта песня уже на экране (иначе мерцает видеофон).
        if (_projectionStateService.Current.SongId == value.SongId)
        {
            return;
        }

        _ = ShowSongSectionsAsync(value);
    }

    [ObservableProperty]
    private LiveSectionItem? selectedSection;

    [ObservableProperty]
    private ProjectionState currentState = ProjectionState.Empty;

    [ObservableProperty]
    private bool isLoading;

    [ObservableProperty]
    private string? statusMessage;

    [ObservableProperty]
    private bool isProjectionWindowOpen;

    [ObservableProperty]
    private bool isShowStarted;

    [ObservableProperty]
    private bool isProjectionCleared;

    [ObservableProperty]
    private int currentSongIndex;

    [ObservableProperty]
    private bool isBlackoutEnabled;

    [ObservableProperty]
    private NdiSource? selectedNdiSource;

    [ObservableProperty]
    private bool isNdiConnected;

    [ObservableProperty]
    private bool isNdiModeActive;

    [ObservableProperty]
    private bool isLoadingNdiSources;

    public string SongProgressLabel => _currentEntries.Count == 0
        ? "Нет активной песни."
        : $"Песня {CurrentSongIndex + 1} из {_currentEntries.Count}";

    public string SectionProgressLabel => _currentSections.Count == 0
        ? "Секция: —"
        : $"Секция: {Math.Min(CurrentState.SectionIndex + 1, _currentSections.Count)} / {_currentSections.Count}";

    public IAsyncRelayCommand RefreshQueueCommand { get; }
    public IRelayCommand ShowNextCommand { get; }
    public IRelayCommand ShowPreviousCommand { get; }
    public IRelayCommand ClearProjectionCommand { get; }
    public IRelayCommand RestoreProjectionCommand { get; }
    public IRelayCommand<int> SkipToSectionCommand { get; }
    public IAsyncRelayCommand OpenProjectionCommand { get; }
    public IAsyncRelayCommand CloseProjectionCommand { get; }
    public IRelayCommand ToggleBlackoutCommand { get; }
    public IRelayCommand<Playlist> LoadSavedPlaylistCommand { get; }
    public IAsyncRelayCommand<Playlist> DeleteSavedPlaylistCommand { get; }
    public IRelayCommand ClearQuickPlaylistCommand { get; }
    public IAsyncRelayCommand<string> SaveQuickPlaylistCommand { get; }
    public IRelayCommand ToggleVideoModeCommand { get; }
    public IRelayCommand ToggleNdiVideoModeCommand { get; }
    public IAsyncRelayCommand RefreshNdiSourcesCommand { get; }

    public LiveControlViewModel(
        ICatalogService catalogService,
        IProjectionStateService projectionStateService,
        IProjectionDisplayService projectionDisplayService,
        IDisplaySettingsService displaySettingsService,
        ICameraStreamService cameraStreamService,
        IBibleService bibleService,
        INdiReceiverService? ndiReceiverService = null)
    {
        _catalogService = catalogService;
        _projectionStateService = projectionStateService;
        _projectionDisplayService = projectionDisplayService;
        _displaySettingsService = displaySettingsService;
        _cameraStreamService = cameraStreamService;
        _bibleService = bibleService;
        _ndiReceiverService = ndiReceiverService;
        _dispatcher = App.MainDispatcherQueue ?? DispatcherQueue.GetForCurrentThread() ?? throw new InvalidOperationException("DispatcherQueue недоступен.");

        RefreshQueueCommand = new AsyncRelayCommand(LoadQueueAsync);
        ShowNextCommand = new RelayCommand(AdvanceOrNextSong);
        ShowPreviousCommand = new AsyncRelayCommand(RewindOrPreviousSongAsync);
        ClearProjectionCommand = new RelayCommand(ClearProjection, () => IsShowStarted && !IsProjectionCleared);
        RestoreProjectionCommand = new RelayCommand(RestoreProjection, () => IsShowStarted && IsProjectionCleared);
        SkipToSectionCommand = new RelayCommand<int>(SkipToSection);
        OpenProjectionCommand = new AsyncRelayCommand(StartShowFromButtonAsync, () => !IsShowStarted);
        CloseProjectionCommand = new AsyncRelayCommand(EndShowAsync, () => _projectionDisplayService.IsOpen);
        ToggleBlackoutCommand = new RelayCommand(ToggleBlackout, () => _projectionDisplayService.IsOpen);
        LoadSavedPlaylistCommand = new RelayCommand<Playlist>(LoadPlaylistIntoQuick, playlist => playlist is not null);
        DeleteSavedPlaylistCommand = new AsyncRelayCommand<Playlist>(DeleteSavedPlaylistAsync, playlist => playlist is not null);
        ClearQuickPlaylistCommand = new RelayCommand(ClearQuickPlaylist, () => QuickEntries.Count > 0);
        SaveQuickPlaylistCommand = new AsyncRelayCommand<string>(SaveQuickPlaylistAsync, name => !string.IsNullOrWhiteSpace(name) && QuickEntries.Count > 0);
        ToggleVideoModeCommand = new RelayCommand(ToggleVideoMode, () => _projectionDisplayService.IsOpen);
        ToggleNdiVideoModeCommand = new RelayCommand(ToggleNdiVideoMode, () => _projectionDisplayService.IsOpen && _ndiReceiverService != null);
        RefreshNdiSourcesCommand = new AsyncRelayCommand(RefreshNdiSourcesAsync, () => _projectionDisplayService.IsOpen && _ndiReceiverService != null);

        QuickEntries.CollectionChanged += OnQuickEntriesCollectionChanged;
    }

    private void OnQuickEntriesCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        ClearQuickPlaylistCommand.NotifyCanExecuteChanged();
        SaveQuickPlaylistCommand.NotifyCanExecuteChanged();
    }

    public async Task InitializeAsync()
    {
        if (_isInitialized)
        {
            return;
        }

        _projectionStateService.StateChanged += ProjectionStateServiceOnStateChanged;
        UpdateFromState(_projectionStateService.Current);

        _projectionDisplayService.ProjectionWindowVisibilityChanged += OnProjectionVisibilityChanged;
        _projectionDisplayService.BlackoutStateChanged += OnBlackoutStateChanged;
        _projectionDisplayService.NdiModeStateChanged += OnNdiModeStateChanged;
        IsProjectionWindowOpen = _projectionDisplayService.IsOpen;
        IsBlackoutEnabled = _projectionDisplayService.IsBlackout;
        IsNdiModeActive = _projectionDisplayService.IsNdiModeActive;

        // Подписываемся на события NDI, если сервис доступен
        if (_ndiReceiverService != null)
        {
            _ndiReceiverService.ConnectionStateChanged += OnNdiConnectionStateChanged;
            IsNdiConnected = _ndiReceiverService.IsConnected;
        }

        _isInitialized = true;
        await LoadQueueAsync();
        await LoadThemeFromSettingsAsync();
        await EnsurePersistentBackgroundAsync();

        // Обновляем команды после инициализации
        ToggleNdiVideoModeCommand.NotifyCanExecuteChanged();
        RefreshNdiSourcesCommand.NotifyCanExecuteChanged();
        
        // Загружаем NDI источники, если проектор открыт
        if (IsProjectionWindowOpen)
        {
            await RefreshNdiSourcesAsync();
        }
    }

    private void OnNdiConnectionStateChanged(object? sender, bool connected)
    {
        try
        {
            if (_dispatcher == null)
            {
                System.Diagnostics.Debug.WriteLine($"[LiveControlViewModel] Dispatcher is null, cannot update IsNdiConnected");
                return;
            }
            
            if (_dispatcher.HasThreadAccess)
            {
                // Уже в UI потоке, обновляем напрямую
                try
                {
                    IsNdiConnected = connected;
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"[LiveControlViewModel] Error updating IsNdiConnected (same thread): {ex.Message}");
                    System.Diagnostics.Debug.WriteLine($"[LiveControlViewModel] StackTrace: {ex.StackTrace}");
                }
            }
            else
            {
                // Вызываем в UI потоке
                var enqueued = _dispatcher.TryEnqueue(() =>
                {
                    try
                    {
                        IsNdiConnected = connected;
                    }
                    catch (Exception ex)
                    {
                        System.Diagnostics.Debug.WriteLine($"[LiveControlViewModel] Error updating IsNdiConnected: {ex.Message}");
                        System.Diagnostics.Debug.WriteLine($"[LiveControlViewModel] StackTrace: {ex.StackTrace}");
                        System.Diagnostics.Debug.WriteLine($"[LiveControlViewModel] Exception type: {ex.GetType().FullName}");
                        if (ex.InnerException != null)
                        {
                            System.Diagnostics.Debug.WriteLine($"[LiveControlViewModel] InnerException: {ex.InnerException.Message}");
                        }
                    }
                });
                
                if (!enqueued)
                {
                    System.Diagnostics.Debug.WriteLine($"[LiveControlViewModel] Failed to enqueue IsNdiConnected update");
                }
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[LiveControlViewModel] Error in OnNdiConnectionStateChanged: {ex.Message}");
            System.Diagnostics.Debug.WriteLine($"[LiveControlViewModel] StackTrace: {ex.StackTrace}");
            System.Diagnostics.Debug.WriteLine($"[LiveControlViewModel] Exception type: {ex.GetType().FullName}");
            if (ex.InnerException != null)
            {
                System.Diagnostics.Debug.WriteLine($"[LiveControlViewModel] InnerException: {ex.InnerException.Message}");
            }
        }
    }

    private void OnNdiModeStateChanged(object? sender, bool isActive)
    {
        try
        {
            if (_dispatcher == null)
            {
                System.Diagnostics.Debug.WriteLine($"[LiveControlViewModel] Dispatcher is null, cannot update IsNdiModeActive");
                return;
            }
            
            if (_dispatcher.HasThreadAccess)
            {
                // Уже в UI потоке, обновляем напрямую
                IsNdiModeActive = isActive;
            }
            else
            {
                // Вызываем в UI потоке
                var enqueued = _dispatcher.TryEnqueue(() =>
                {
                    try
                    {
                        IsNdiModeActive = isActive;
                    }
                    catch (Exception ex)
                    {
                        System.Diagnostics.Debug.WriteLine($"[LiveControlViewModel] Error updating IsNdiModeActive: {ex.Message}");
                        System.Diagnostics.Debug.WriteLine($"[LiveControlViewModel] StackTrace: {ex.StackTrace}");
                    }
                });
                
                if (!enqueued)
                {
                    System.Diagnostics.Debug.WriteLine($"[LiveControlViewModel] Failed to enqueue IsNdiModeActive update");
                }
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[LiveControlViewModel] Error in OnNdiModeStateChanged: {ex.Message}");
            System.Diagnostics.Debug.WriteLine($"[LiveControlViewModel] StackTrace: {ex.StackTrace}");
        }
    }

    public async Task LoadThemeFromSettingsAsync()
    {
        try
        {
            var themePresetId = await _displaySettingsService.GetSelectedThemePresetIdAsync();
            System.Diagnostics.Debug.WriteLine($"LoadThemeFromSettingsAsync: themePresetId = {themePresetId}");
            
            if (themePresetId.HasValue)
            {
                var themePreset = await _catalogService.GetThemePresetAsync(themePresetId.Value);
                if (themePreset is not null)
                {
                    System.Diagnostics.Debug.WriteLine($"LoadThemeFromSettingsAsync: Загружен стиль '{themePreset.Name}', Primary={themePreset.Colors.Primary}, Background={themePreset.Colors.Background}");
                    _currentThemePreset = themePreset;
                    _projectionDisplayService.ApplyTheme(themePreset);
                }
                else
                {
                    System.Diagnostics.Debug.WriteLine($"LoadThemeFromSettingsAsync: Стиль с ID {themePresetId.Value} не найден в базе данных");
                    _currentThemePreset = null;
                    _projectionDisplayService.ApplyTheme(null);
                }
            }
            else
            {
                System.Diagnostics.Debug.WriteLine($"LoadThemeFromSettingsAsync: Стиль не выбран в настройках, применяем стиль по умолчанию");
                // Если стиль не выбран, применяем стиль по умолчанию (null)
                _currentThemePreset = null;
                _projectionDisplayService.ApplyTheme(null);
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Ошибка при загрузке стиля из настроек: {ex.Message}, StackTrace: {ex.StackTrace}");
            // Применяем стиль по умолчанию при ошибке
            _currentThemePreset = null;
            _projectionDisplayService.ApplyTheme(null);
        }
    }

    private async Task LoadQueueAsync()
    {
        if (IsLoading)
        {
            return;
        }

        try
        {
            IsLoading = true;
            StatusMessage = "Загружаем сет-листы и события...";

            Queue.Clear();
            SavedPlaylists.Clear();
            var playlists = await _catalogService.GetPlaylistsAsync();

            foreach (var playlist in playlists.OrderByDescending(p => p.ScheduledAt ?? DateTime.MaxValue))
            {
                var scheduleLabel = playlist.ScheduledAt.HasValue
                    ? playlist.ScheduledAt.Value.ToString("g", CultureInfo.CurrentCulture)
                    : "Без даты";
                var entries = playlist.Entries
                    .OrderBy(entry => entry.Order)
                    .ToList();

                Queue.Add(new LiveQueueEntry(
                    playlist.Id,
                    playlist.Name,
                    playlist.ScheduledAt,
                    scheduleLabel,
                    entries,
                    playlist.ThemePreset));

                SavedPlaylists.Add(playlist);
            }

            StatusMessage = Queue.Count == 0
                ? "Нет готовых сет-листов. Импортируйте песни или создайте плейлист."
                : $"Доступно {Queue.Count} сетов. Выберите сет, чтобы подготовить вывод.";
        }
        catch (Exception ex)
        {
            StatusMessage = null;
            await ErrorDialog.ShowAsync("Ошибка загрузки трансляции", ex);
        }
        finally
        {
            IsLoading = false;
        }
    }

    private void ProjectionStateServiceOnStateChanged(object? sender, ProjectionState state)
    {
        if (_dispatcher.HasThreadAccess)
        {
            UpdateFromState(state);
        }
        else
        {
            _dispatcher.TryEnqueue(() => UpdateFromState(state));
        }
    }

    private void UpdateFromState(ProjectionState state)
    {
        CurrentState = state;
        VisibleLines.Clear();

        foreach (var line in state.VisibleLines)
        {
            VisibleLines.Add(line);
        }

        UpdateSectionsHighlight(state.SectionIndex);
        NotifySectionProgressChanged();
    }

    partial void OnSelectedEntryChanged(LiveQueueEntry? value)
    {
        _ = OnSelectedEntryChangedAsync(value);
    }

    private async Task OnSelectedEntryChangedAsync(LiveQueueEntry? value)
    {
        _currentEntries.Clear();
        _currentSections.Clear();
        Sections.Clear();
        CurrentSongIndex = 0;

        if (value is null)
        {
            _projectionStateService.Clear();
            NotifySongProgressChanged();
            return;
        }

        StatusMessage = $"Сет «{value.Title}» выбран для предпросмотра.";

        _projectionStateService.SetPlaylistContext(value.PlaylistId);

        foreach (var entry in value.Entries.OrderBy(entry => entry.Order))
        {
            if (entry.Song is not null)
            {
                _currentEntries.Add(entry);
            }
        }

        // Стиль теперь берётся из настроек, а не из плейлиста
        // _projectionDisplayService.ApplyTheme(value.ThemePreset);

        if (_currentEntries.Count == 0)
        {
            _projectionStateService.Clear();
            NotifySongProgressChanged();
            return;
        }

        await MoveToEntryAsync(0);
    }

    private async Task OpenProjectionAsync()
    {
        var alreadyOpen = _projectionDisplayService.IsOpen;

        System.Diagnostics.Debug.WriteLine($"OpenProjectionAsync: alreadyOpen={alreadyOpen}");

        // Если окно уже открыто (например, через постоянный фон), не вызываем ShowAsync
        // чтобы избежать повторного применения темы и чёрного мерцания
        if (!alreadyOpen)
        {
            await _projectionDisplayService.ShowAsync();
        }

        // Тема применяется внутри ShowAsync через ApplySavedThemeAsync
        // Дублирование здесь вызывает проблемы с чёрным фоном при первом запуске

        // Отмечаем выбранную песню как проигранную при открытии трансляции
        try
        {
            if (SelectedQuickEntry?.SongId != Guid.Empty)
            {
                SelectedQuickEntry.WasPlayed = true;
                _ = RecordPlaySafeAsync(SelectedQuickEntry.SongId);
            }
        }
        catch
        {
            // Игнорируем ошибки при записи статистики
        }

        // NDI не блокирует открытие окна
        _ = RefreshNdiSourcesAsync();
    }

    private void CloseProjection()
    {
        IsShowStarted = false;
        IsProjectionCleared = false;
        _projectionStateService.Clear();
        _projectionDisplayService.Hide();
    }

    /// <summary>
    /// Esc / завершение показа: при постоянном фоне — только убрать текст, иначе закрыть окно.
    /// </summary>
    private async Task EndShowAsync()
    {
        if (!_projectionDisplayService.IsOpen)
        {
            return;
        }

        try
        {
            if (await _displaySettingsService.GetKeepProjectionBackgroundAsync())
            {
                IsShowStarted = false;
                IsProjectionCleared = false;
                _projectionDisplayService.SetBlackout(false);
                _projectionStateService.Clear();
                StatusMessage = "Текст убран. Фон остаётся на экране.";
            }
            else
            {
                CloseProjection();
                StatusMessage = "Трансляция завершена.";
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"EndShowAsync: {ex.Message}");
            CloseProjection();
        }
    }

    /// <summary>
    /// Если включён постоянный фон — открыть окно без текста (для старта приложения / настройки).
    /// </summary>
    public async Task EnsurePersistentBackgroundAsync()
    {
        try
        {
            if (!await _displaySettingsService.GetKeepProjectionBackgroundAsync())
            {
                return;
            }

            if (!await _displaySettingsService.CanKeepProjectionBackgroundAsync())
            {
                // На одном/основном экране опция опасна — выключаем и не открываем окно.
                await _displaySettingsService.SetKeepProjectionBackgroundAsync(false);
                if (_projectionDisplayService.IsOpen)
                {
                    _projectionDisplayService.Hide();
                }

                return;
            }

            // Не очищаем контент перед открытием - это вызывает мерцание чёрным экраном
            // Фон уже должен быть установлен через ApplyTheme
            _projectionDisplayService.SetBlackout(false);

            if (!_projectionDisplayService.IsOpen)
            {
                await OpenProjectionAsync();
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"EnsurePersistentBackgroundAsync: {ex.Message}");
        }
    }

    /// <summary>
    /// Выполнение действий трансляции по горячим клавишам (минуя CanExecute UI-команд).
    /// </summary>
    public void ExecuteHotkey(AppHotkeyAction action)
    {
        switch (action)
        {
            case AppHotkeyAction.StartShow:
                _ = StartShowFromHotkeyAsync();
                break;
            case AppHotkeyAction.EndShow:
                _ = EndShowAsync();
                break;
            case AppHotkeyAction.NextSlide:
                AdvanceOrNextSong();
                break;
            case AppHotkeyAction.PreviousSlide:
                _ = RewindOrPreviousSongAsync();
                break;
        }
    }

    /// <summary>
    /// Кнопка «Начать показ».
    /// В каталоге с выбранной песней — всегда запускаем именно её (и открываем Трансляцию).
    /// Иначе — продолжаем текущий контент / быстрый плейлист / пустой проектор.
    /// </summary>
    private async Task StartShowFromButtonAsync()
    {
        await StartShowFromHotkeyAsync();
    }

    /// <summary>
    /// F5 / «Начать показ».
    /// В каталоге с выбранной песней — всегда запускаем именно её (и открываем Трансляцию).
    /// Иначе — продолжаем текущий контент / быстрый плейлист / пустой проектор.
    /// </summary>
    private async Task StartShowFromHotkeyAsync()
    {
        if (_startShowInProgress)
        {
            return;
        }

        _startShowInProgress = true;
        try
        {
            if (!_isInitialized)
            {
                await InitializeAsync();
            }

            var main = App.AppHost.Services.GetService(typeof(MainViewModel)) as MainViewModel;
            var catalog = App.AppHost.Services.GetService(typeof(CatalogViewModel)) as CatalogViewModel;

            // Главный сценарий: старт из каталога — с выбранного куплета
            if (main?.IsOnCatalogPage == true && catalog?.SelectedSong is { } catalogSong)
            {
                var sectionIndex = catalog.SelectedSectionPreview?.ListIndex ?? 0;
                await StartSongFromCatalogAsync(catalogSong, sectionIndex);
                return;
            }

            // Старт из раздела Библии
            if (main?.IsOnBiblePage == true)
            {
                var bible = App.AppHost.Services.GetService(typeof(BibleViewModel)) as BibleViewModel;
                if (bible is not null)
                {
                    await bible.StartProjectionFromHotkeyAsync();
                    return;
                }
            }

            // Старт из раздела Объявления
            if (main?.IsOnAnnouncementsPage == true)
            {
                var announcements = App.AppHost.Services.GetService(typeof(AnnouncementsViewModel)) as AnnouncementsViewModel;
                if (announcements is not null)
                {
                    await announcements.StartProjectionFromHotkeyAsync();
                    return;
                }
            }

            // Быстрый плейлист / текущая песня на Трансляции:
            // всегда выкладываем выбранную секцию (после Esc при постоянном фоне
            // SongId уже null, а _currentSections ещё заполнены — старый early-return ничего не делал).
            var entryForShow = ResolveEntryForHotkeyStart();
            if (entryForShow?.Song is not null)
            {
                // Устанавливаем флаг реального показа ДО вызова ShowSongSectionsAsync
                IsShowStarted = true;

                await ShowSongSectionsAsync(entryForShow, ResolveSectionIndexForHotkeyStart(entryForShow), forceShow: true);
                if (!_projectionDisplayService.IsOpen)
                {
                    await OpenProjectionAsync();
                }

                // Отмечаем песню как проигранную при запуске через горячую клавишу
                if (entryForShow.SongId != Guid.Empty)
                {
                    entryForShow.WasPlayed = true;
                    _ = RecordPlaySafeAsync(entryForShow.SongId);
                }

                return;
            }

            // Контент в state без записи в UI — просто открыть окно и гарантировать видимость текста
            if (_projectionStateService.Current.SongId is not null)
            {
                if (!_projectionDisplayService.IsOpen)
                {
                    await OpenProjectionAsync();
                }

                _projectionDisplayService.EnsureContentVisible();
                return;
            }

            // Фоллбек: если где-то выбрана песня в каталоге
            if (catalog?.SelectedSong is { } fallbackSong)
            {
                await StartSongFromCatalogAsync(fallbackSong);
                return;
            }

            if (!_projectionDisplayService.IsOpen)
            {
                await OpenProjectionAsync();
            }
        }
        catch (Exception ex)
        {
            StatusMessage = null;
            await ErrorDialog.ShowAsync("Не удалось начать показ", ex);
            System.Diagnostics.Debug.WriteLine($"StartShowFromHotkeyAsync: {ex}");
        }
        finally
        {
            _startShowInProgress = false;
        }
    }

    private void ClearProjection()
    {
        _projectionStateService.SetLinesOverride(Array.Empty<string>());
        IsProjectionCleared = true;
        NotifySectionProgressChanged();
    }

    private void RestoreProjection()
    {
        _projectionStateService.ClearLinesOverride();
        IsProjectionCleared = false;
        NotifySectionProgressChanged();
    }

    private void RestoreProjectionIfCleared()
    {
        if (!IsProjectionCleared)
        {
            return;
        }

        RestoreProjection();
    }

    private PlaylistEntry? ResolveEntryForHotkeyStart()
    {
        if (SelectedQuickEntry?.Song is not null)
        {
            return SelectedQuickEntry;
        }

        if (CurrentSongIndex >= 0
            && CurrentSongIndex < _currentEntries.Count
            && _currentEntries[CurrentSongIndex].Song is not null)
        {
            return _currentEntries[CurrentSongIndex];
        }

        return QuickEntries.FirstOrDefault(e => e.Song is not null);
    }

    private int ResolveSectionIndexForHotkeyStart(PlaylistEntry entry)
    {
        if (SelectedSection is not null && Sections.Count > 0)
        {
            var idx = Sections.IndexOf(SelectedSection);
            if (idx >= 0)
            {
                return idx;
            }
        }

        if (_projectionStateService.Current.SongId == entry.SongId)
        {
            return Math.Max(0, _projectionStateService.Current.SectionIndex);
        }

        return 0;
    }

    private static bool IsBibleProjection(Song? song) =>
        song?.DefaultKey?.StartsWith("bible:", StringComparison.OrdinalIgnoreCase) == true
        || song?.Subtitle?.Contains("Синодальный", StringComparison.OrdinalIgnoreCase) == true;

    private static bool IsAnnouncementProjection(Song? song) =>
        song?.Subtitle?.Equals("Объявление", StringComparison.OrdinalIgnoreCase) == true;

    private static ProjectionContentKind ResolveProjectionContentKind(Song? song)
    {
        if (IsAnnouncementProjection(song))
        {
            return ProjectionContentKind.Announcement;
        }

        if (IsBibleProjection(song))
        {
            return ProjectionContentKind.Bible;
        }

        return ProjectionContentKind.Song;
    }

    private static bool TryParseBibleProjection(Song? song, out string bookId, out int chapter)
    {
        bookId = string.Empty;
        chapter = 0;
        if (song?.DefaultKey is not string key
            || !key.StartsWith("bible:", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        // bible:{bookId}:{chapter} — bookId без двоеточий (Gen, 1Sam, …)
        var parts = key.Split(':', 3, StringSplitOptions.None);
        if (parts.Length < 3
            || string.IsNullOrWhiteSpace(parts[1])
            || !int.TryParse(parts[2], out chapter)
            || chapter <= 0)
        {
            return false;
        }

        bookId = parts[1];
        return true;
    }

    private Song? GetCurrentProjectionSong()
    {
        if (SelectedQuickEntry?.Song is not null)
        {
            return SelectedQuickEntry.Song;
        }

        if (CurrentSongIndex >= 0 && CurrentSongIndex < _currentEntries.Count)
        {
            return _currentEntries[CurrentSongIndex].Song;
        }

        return null;
    }

    private PlaylistEntry? GetCurrentPlaylistEntry()
    {
        if (SelectedQuickEntry is not null)
        {
            return SelectedQuickEntry;
        }

        if (CurrentSongIndex >= 0 && CurrentSongIndex < _currentEntries.Count)
        {
            return _currentEntries[CurrentSongIndex];
        }

        return null;
    }

    /// <summary>
    /// На последнем/первом стихе главы — перейти к соседней главе (без закрытия окна).
    /// В конце/начале Библии остаёмся на месте.
    /// </summary>
    private async Task<bool> TryContinueBibleChapterAsync(int direction)
    {
        var currentSong = GetCurrentProjectionSong();
        if (!TryParseBibleProjection(currentSong, out var bookId, out var chapter))
        {
            return false;
        }

        await _bibleService.EnsureLoadedAsync();
        var books = _bibleService.GetBooks();
        if (books.Count == 0)
        {
            return true;
        }

        var bookIndex = -1;
        for (var i = 0; i < books.Count; i++)
        {
            if (string.Equals(books[i].BookId, bookId, StringComparison.OrdinalIgnoreCase))
            {
                bookIndex = i;
                break;
            }
        }

        if (bookIndex < 0)
        {
            return true;
        }

        var chapters = _bibleService.GetChapters(bookId);
        var chapterIndex = -1;
        for (var i = 0; i < chapters.Count; i++)
        {
            if (chapters[i] == chapter)
            {
                chapterIndex = i;
                break;
            }
        }

        string nextBookId;
        int nextChapter;
        var startFromLast = false;

        if (direction > 0)
        {
            if (chapterIndex >= 0 && chapterIndex < chapters.Count - 1)
            {
                nextBookId = bookId;
                nextChapter = chapters[chapterIndex + 1];
            }
            else if (bookIndex < books.Count - 1)
            {
                nextBookId = books[bookIndex + 1].BookId;
                var nextChapters = _bibleService.GetChapters(nextBookId);
                if (nextChapters.Count == 0)
                {
                    StatusMessage = "Дальше глав нет — трансляция остаётся открытой.";
                    return true;
                }

                nextChapter = nextChapters[0];
            }
            else
            {
                StatusMessage = "Конец Библии — трансляция остаётся открытой.";
                return true;
            }
        }
        else
        {
            if (chapterIndex > 0)
            {
                nextBookId = bookId;
                nextChapter = chapters[chapterIndex - 1];
                startFromLast = true;
            }
            else if (bookIndex > 0)
            {
                nextBookId = books[bookIndex - 1].BookId;
                var prevChapters = _bibleService.GetChapters(nextBookId);
                if (prevChapters.Count == 0)
                {
                    StatusMessage = "Раньше глав нет — трансляция остаётся открытой.";
                    return true;
                }

                nextChapter = prevChapters[^1];
                startFromLast = true;
            }
            else
            {
                StatusMessage = "Начало Библии — трансляция остаётся открытой.";
                return true;
            }
        }

        var bibleVm = App.AppHost.Services.GetService(typeof(BibleViewModel)) as BibleViewModel;
        var song = bibleVm?.BuildChapterProjectionSong(nextBookId, nextChapter)
                   ?? BuildBibleSongFallback(nextBookId, nextChapter);
        if (song is null)
        {
            return true;
        }

        await ReplaceCurrentBibleProjectionAsync(song, startFromLast);
        return true;
    }

    private Song? BuildBibleSongFallback(string bookId, int chapter)
    {
        var book = _bibleService.GetBooks()
            .FirstOrDefault(b => string.Equals(b.BookId, bookId, StringComparison.OrdinalIgnoreCase));
        if (book is null)
        {
            return null;
        }

        var passage = _bibleService.GetPassage(bookId, chapter, 1);
        if (passage.Count == 0)
        {
            return null;
        }

        var songId = Guid.NewGuid();
        var sections = passage
            .Select((v, index) => new SongSection
            {
                Id = Guid.NewGuid(),
                SongId = songId,
                Order = index,
                SectionType = SectionType.Custom,
                Heading = $"{book.RussianName} {v.Chapter}:{v.Verse}",
                Content = $"{v.Verse}  {v.Text}"
            })
            .ToList();

        var title = passage.Count > 1
            ? $"{book.RussianName} {chapter}:{passage[0].Verse}–{passage[^1].Verse}"
            : $"{book.RussianName} {chapter}:{passage[0].Verse}";

        return new Song
        {
            Id = songId,
            Title = title,
            Subtitle = $"{book.RussianName} · Синодальный",
            Language = "ru",
            DefaultKey = $"bible:{book.BookId}:{chapter}",
            Sections = sections
        };
    }

    private async Task ReplaceCurrentBibleProjectionAsync(Song song, bool startFromLastSection)
    {
        var entry = GetCurrentPlaylistEntry();
        if (entry is null)
        {
            await StartSongFromCatalogAsync(song);
            return;
        }

        entry.SongId = song.Id;
        entry.Song = song;

        // Обновляем очередь, если текущий элемент там есть
        var queueIdx = _currentEntries.FindIndex(e => ReferenceEquals(e, entry) || e.Id == entry.Id);
        if (queueIdx < 0)
        {
            _currentEntries.Clear();
            foreach (var quick in QuickEntries.Where(e => e.Song is not null).OrderBy(e => e.Order))
            {
                _currentEntries.Add(quick);
            }

            CurrentSongIndex = Math.Max(0, _currentEntries.FindIndex(e => e.Id == entry.Id));
        }
        else
        {
            CurrentSongIndex = queueIdx;
        }

        // Устанавливаем флаг реального показа ДО вызова ShowSongSectionsAsync
        IsShowStarted = true;

        _projectionStateService.Clear();
        await ShowSongSectionsAsync(entry, forceShow: true);

        if (startFromLastSection && _currentSections.Count > 0)
        {
            _projectionStateService.GoToSection(_currentSections.Count - 1);
        }

        _suppressQuickEntryShow = true;
        SelectedQuickEntry = entry;
        _suppressQuickEntryShow = false;

        StatusMessage = $"На экране: {song.Title}";
        NotifySongProgressChanged();
        NotifySectionProgressChanged();
    }

    private void ClearQuickPlaylist()
    {
        QuickEntries.Clear();
        SelectedEntry = null;
        StatusMessage = "Быстрый плейлист очищен.";
    }

    /// <summary>
    /// Запускает песню из каталога в трансляции: секции, быстрый плейлист, окно проекции.
    /// Всегда стартует переданную песню, даже если в плейлисте уже есть другая.
    /// </summary>
    /// <param name="startSectionIndex">Индекс секции (0-based в порядке Order), с которой начать показ.</param>
    public async Task StartSongFromCatalogAsync(Song song, int startSectionIndex = 0)
    {
        if (song is null)
        {
            return;
        }

        // Сразу переключаем UI на «Трансляцию» — без ожидания проектора/темы/очереди
        var main = App.AppHost.Services.GetService(typeof(MainViewModel)) as MainViewModel;
        main?.NavigateToLiveControl();

        EnsureInitializedLightweight();

        // Эфемерный контент (Библия и т.п.) уже с секциями — не ходим в БД
        Song fullSong;
        if (song.Sections is { Count: > 0 })
        {
            fullSong = song;
        }
        else
        {
            fullSong = await _catalogService.GetSongAsync(song.Id) ?? song;
        }

        if (fullSong.Sections is null || fullSong.Sections.Count == 0)
        {
            StatusMessage = $"У песни «{fullSong.Title}» нет секций для показа.";
            System.Diagnostics.Debug.WriteLine($"StartSongFromCatalogAsync: song {fullSong.Id} has no sections");
        }

        // Добавляем в быстрый плейлист, если ещё нет
        var entry = QuickEntries.FirstOrDefault(e => e.SongId == fullSong.Id);
        if (entry is null)
        {
            entry = new PlaylistEntry
            {
                Id = Guid.NewGuid(),
                SongId = fullSong.Id,
                Song = fullSong,
                Order = QuickEntries.Count
            };
            QuickEntries.Add(entry);
        }
        else
        {
            entry.Song = fullSong;
        }

        // Синхронизируем очередь для ←/→
        _currentEntries.Clear();
        foreach (var quick in QuickEntries.Where(e => e.Song is not null).OrderBy(e => e.Order))
        {
            _currentEntries.Add(quick);
        }

        CurrentSongIndex = Math.Max(0, _currentEntries.FindIndex(e => e.SongId == fullSong.Id));

        // Устанавливаем флаг реального показа ДО вызова ShowSongSectionsAsync
        IsShowStarted = true;

        // Сбрасываем старое состояние проектора и ставим новую песню с нужной секции
        _projectionStateService.Clear();
        await ShowSongSectionsAsync(entry, startSectionIndex, forceShow: true);

        if (!_projectionDisplayService.IsOpen)
        {
            await OpenProjectionAsync();
        }

        // Отмечаем песню как проигранную только при запуске в трансляцию
        if (entry.SongId != Guid.Empty)
        {
            entry.WasPlayed = true;
            _ = RecordPlaySafeAsync(entry.SongId);
        }

        // Всегда выделяем запущенную песню в быстром плейлисте
        _suppressQuickEntryShow = true;
        SelectedQuickEntry = entry;
        _suppressQuickEntryShow = false;

        StatusMessage = $"Песня «{fullSong.Title}» в трансляции.";
        NotifySongProgressChanged();
    }

    /// <summary>
    /// Подписки без ожидания LoadQueue / темы — для быстрого старта из каталога.
    /// </summary>
    private void EnsureInitializedLightweight()
    {
        if (_isInitialized)
        {
            return;
        }

        _projectionStateService.StateChanged += ProjectionStateServiceOnStateChanged;
        UpdateFromState(_projectionStateService.Current);

        _projectionDisplayService.ProjectionWindowVisibilityChanged += OnProjectionVisibilityChanged;
        _projectionDisplayService.BlackoutStateChanged += OnBlackoutStateChanged;
        _projectionDisplayService.NdiModeStateChanged += OnNdiModeStateChanged;
        IsProjectionWindowOpen = _projectionDisplayService.IsOpen;
        IsBlackoutEnabled = _projectionDisplayService.IsBlackout;
        IsNdiModeActive = _projectionDisplayService.IsNdiModeActive;

        if (_ndiReceiverService != null)
        {
            _ndiReceiverService.ConnectionStateChanged += OnNdiConnectionStateChanged;
            IsNdiConnected = _ndiReceiverService.IsConnected;
        }

        _isInitialized = true;
        ToggleNdiVideoModeCommand.NotifyCanExecuteChanged();
        RefreshNdiSourcesCommand.NotifyCanExecuteChanged();

        // Очередь и тема — в фоне
        _ = LoadQueueAsync();
        _ = LoadThemeFromSettingsAsync();
    }

    public void AddSongToQuickPlaylist(Song song)
    {
        if (song is null)
        {
            return;
        }

        // Проверяем, не добавлена ли уже эта песня
        if (QuickEntries.Any(e => e.SongId == song.Id))
        {
            StatusMessage = $"Песня «{song.Title}» уже в быстром плейлисте.";
            return;
        }

        var entry = new PlaylistEntry
        {
            Id = Guid.NewGuid(),
            SongId = song.Id,
            Song = song,
            Order = QuickEntries.Count
        };

        QuickEntries.Add(entry);
        StatusMessage = $"Песня «{song.Title}» добавлена в быстрый плейлист.";
    }

    public bool IsSongInQuickPlaylist(Guid songId) =>
        QuickEntries.Any(e => e.SongId == songId);

    public void RemoveQuickEntry(PlaylistEntry entry)
    {
        if (entry is null || !QuickEntries.Contains(entry))
        {
            return;
        }

        var title = entry.Song?.Title ?? "Позиция";
        var wasSelected = SelectedQuickEntry == entry;
        QuickEntries.Remove(entry);

        for (var i = 0; i < QuickEntries.Count; i++)
        {
            QuickEntries[i].Order = i;
        }

        _currentEntries.Clear();
        foreach (var quick in QuickEntries.Where(e => e.Song is not null).OrderBy(e => e.Order))
        {
            _currentEntries.Add(quick);
        }

        if (wasSelected)
        {
            _suppressQuickEntryShow = true;
            SelectedQuickEntry = QuickEntries.FirstOrDefault();
            _suppressQuickEntryShow = false;
        }

        if (_currentEntries.Count > 0)
        {
            CurrentSongIndex = Math.Clamp(CurrentSongIndex, 0, _currentEntries.Count - 1);
            if (SelectedQuickEntry is not null)
            {
                var idx = _currentEntries.FindIndex(e => e.SongId == SelectedQuickEntry.SongId);
                if (idx >= 0)
                {
                    CurrentSongIndex = idx;
                }
            }
        }
        else
        {
            CurrentSongIndex = 0;
        }

        NotifySongProgressChanged();
        StatusMessage = $"«{title}» удалена из быстрого плейлиста.";
    }

    public async void ShowSongSections(PlaylistEntry? entry)
    {
        await ShowSongSectionsAsync(entry);
    }

    public async Task ShowSongSectionsAsync(PlaylistEntry? entry, int startSectionIndex = 0, bool forceShow = false)
    {
        if (entry?.Song is null)
        {
            return;
        }

        // Если секции не загружены, загружаем их
        if (entry.Song.Sections == null || entry.Song.Sections.Count == 0)
        {
            var song = await _catalogService.GetSongAsync(entry.SongId);
            if (song != null)
            {
                entry.Song = song;
            }
        }

        // Очищаем текущие секции
        _currentSections.Clear();
        Sections.Clear();

        // Загружаем секции выбранной песни
        var sections = entry.Song.Sections?
            .OrderBy(section => section.Order)
            .ToList() ?? new List<SongSection>();

        foreach (var section in sections)
        {
            var snapshot = new SectionSnapshot(section);
            _currentSections.Add(snapshot);
            Sections.Add(new LiveSectionItem(snapshot.Index, snapshot.Title, snapshot.Content, snapshot.Notes));
        }

        // Синхронизируем очередь песен из быстрого плейлиста,
        // чтобы ←/→ могли переходить к соседним песням.
        if (_currentEntries.Count == 0 && QuickEntries.Count > 0)
        {
            foreach (var quick in QuickEntries.Where(e => e.Song is not null).OrderBy(e => e.Order))
            {
                _currentEntries.Add(quick);
            }

            CurrentSongIndex = Math.Max(0, _currentEntries.FindIndex(e => e.SongId == entry.SongId));
        }
        else if (_currentEntries.Count > 0)
        {
            var idx = _currentEntries.FindIndex(e => e.SongId == entry.SongId);
            if (idx >= 0)
            {
                CurrentSongIndex = idx;
            }
        }

        // Индексы секций в UI и в проекторе совпадают (полный список по Order)
        var contentSegments = sections
            .Select(section =>
                !string.IsNullOrWhiteSpace(section.Content)
                    ? section.Content!
                    : (section.Heading ?? string.Empty))
            .ToList();

        IReadOnlyList<string?>? captions = null;
        if (IsBibleProjection(entry.Song))
        {
            captions = sections
                .Select(section => (string?)section.Heading)
                .ToList();
        }

        var initialIndex = contentSegments.Count == 0
            ? 0
            : Math.Clamp(startSectionIndex, 0, contentSegments.Count - 1);

        System.Diagnostics.Debug.WriteLine(
            $"ShowSongSectionsAsync: song='{entry.Song.Title}', sections={sections.Count}, start={initialIndex}");

        var current = _projectionStateService.Current;
        var alreadyOnScreen = current.SongId == entry.SongId
            && current.SectionIndex == initialIndex
            && current.VisibleLines.Count > 0;

        System.Diagnostics.Debug.WriteLine($"ShowSongSectionsAsync: current.SongId={current.SongId}, entry.SongId={entry.SongId}, current.SectionIndex={current.SectionIndex}, initialIndex={initialIndex}, current.VisibleLines.Count={current.VisibleLines.Count}, alreadyOnScreen={alreadyOnScreen}");
        ChyguiSlide.Data.InteractionLogger.Log($"ShowSongSectionsAsync: current.SongId={current.SongId}, entry.SongId={entry.SongId}, current.SectionIndex={current.SectionIndex}, initialIndex={initialIndex}, current.VisibleLines.Count={current.VisibleLines.Count}, alreadyOnScreen={alreadyOnScreen}");

        if (alreadyOnScreen)
        {
            System.Diagnostics.Debug.WriteLine($"ShowSongSectionsAsync: alreadyOnScreen=true, returning early");
            ChyguiSlide.Data.InteractionLogger.Log($"ShowSongSectionsAsync: alreadyOnScreen=true, returning early");
            UpdateSectionsHighlight(current.SectionIndex);
            StatusMessage = sections.Count > 0
                ? $"Песня «{entry.Song.Title}» выбрана. Загружено {sections.Count} секций."
                : $"Песня «{entry.Song.Title}» выбрана. Секций нет.";
            NotifySectionProgressChanged();
            NotifySongProgressChanged();
            // Тот же слайд в state, но на экране мог остаться opacity=0 после сбоя анимации
            _projectionDisplayService.EnsureContentVisible();
            return;
        }

        // Если включена опция "Держать фон на экране" и это не принудительный показ и показ не запущен,
        // не показываем контент на экране при простом выборе секции
        var keepBackground = await _displaySettingsService.GetKeepProjectionBackgroundAsync();
        System.Diagnostics.Debug.WriteLine($"ShowSongSectionsAsync: keepBackground={keepBackground}, forceShow={forceShow}, IsShowStarted={IsShowStarted}, initialIndex={initialIndex}");

        if (keepBackground && !forceShow && !IsShowStarted)
        {
            // Только загружаем секции в UI, но не выводим на экран
            System.Diagnostics.Debug.WriteLine($"ShowSongSectionsAsync: Skipping projection, updating highlight to {initialIndex}");
            UpdateSectionsHighlight(initialIndex);
            StatusMessage = sections.Count > 0
                ? $"Песня «{entry.Song.Title}» выбрана. Загружено {sections.Count} секций."
                : $"Песня «{entry.Song.Title}» показана. Секций нет.";
            NotifySectionProgressChanged();
            NotifySongProgressChanged();
            return;
        }

        // Устанавливаем песню в проектор сразу с нужной секции
        _projectionStateService.SetPlaylistContext(null);
        _projectionStateService.SetSong(
            entry.SongId,
            entry.Song.Title,
            contentSegments,
            initialIndex,
            captions,
            ResolveProjectionContentKind(entry.Song));
        UpdateSectionsHighlight(initialIndex);

        // Фон/тема уже на проекторе при открытом окне (постоянный фон) —
        // повторный ApplyTheme гасит видеофон. Применяем только если окна ещё нет.
        var isOpen = _projectionDisplayService.IsOpen;
        System.Diagnostics.Debug.WriteLine($"ShowSongSectionsAsync: IsOpen={isOpen}, skipping theme application");
        if (!isOpen)
        {
            if (_currentThemePreset is not null)
            {
                _projectionDisplayService.ApplyTheme(_currentThemePreset);
            }
            else
            {
                await LoadThemeFromSettingsAsync();
            }
        }

        StatusMessage = sections.Count > 0
            ? $"Песня «{entry.Song.Title}» выбрана. Загружено {sections.Count} секций."
            : $"Песня «{entry.Song.Title}» выбрана. Секций нет.";
        NotifySectionProgressChanged();
        NotifySongProgressChanged();
    }

    public void SyncQuickPlaylistOrderFromUi()
    {
        for (var i = 0; i < QuickEntries.Count; i++)
        {
            QuickEntries[i].Order = i;
        }

        if (_currentEntries.Count > 0)
        {
            _currentEntries.Clear();
            foreach (var quick in QuickEntries.Where(e => e.Song is not null).OrderBy(e => e.Order))
            {
                _currentEntries.Add(quick);
            }

            if (SelectedQuickEntry is not null)
            {
                var idx = _currentEntries.FindIndex(e => e.SongId == SelectedQuickEntry.SongId);
                if (idx >= 0)
                {
                    CurrentSongIndex = idx;
                }
            }
        }

        NotifySongProgressChanged();
    }

    private async Task RecordPlaySafeAsync(Guid songId)
    {
        try
        {
            await _catalogService.RecordSongPlayAsync(songId);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"RecordSongPlayAsync: {ex.Message}");
        }
    }

    private async Task SaveQuickPlaylistAsync(string? playlistName)
    {
        if (string.IsNullOrWhiteSpace(playlistName) || QuickEntries.Count == 0)
        {
            return;
        }

        try
        {
            IsLoading = true;
            StatusMessage = "Сохраняем плейлист...";

            var persistableEntries = new List<PlaylistEntry>();
            var skippedTitles = new List<string>();

            foreach (var entry in QuickEntries)
            {
                var song = await _catalogService.GetSongAsync(entry.SongId);
                if (song is not null)
                {
                    persistableEntries.Add(entry);
                }
                else if (!string.IsNullOrWhiteSpace(entry.Song?.Title))
                {
                    skippedTitles.Add(entry.Song.Title);
                }
            }

            if (persistableEntries.Count == 0)
            {
                StatusMessage = "В быстром плейлисте нет песен из каталога для сохранения.";
                return;
            }

            var playlist = new Playlist
            {
                Id = Guid.NewGuid(),
                Name = playlistName.Trim(),
                Entries = new List<PlaylistEntry>()
            };

            var order = 0;
            foreach (var entry in persistableEntries)
            {
                var newEntry = new PlaylistEntry
                {
                    Id = Guid.NewGuid(),
                    PlaylistId = playlist.Id,
                    SongId = entry.SongId,
                    AttachmentId = entry.AttachmentId,
                    Order = order++,
                    TransposeSteps = entry.TransposeSteps,
                    TempoOverride = entry.TempoOverride,
                    Cues = entry.Cues
                };
                playlist.Entries.Add(newEntry);
            }

            var savedPlaylist = await _catalogService.UpsertPlaylistAsync(playlist);

            // Обновляем список без блокировки LoadQueueAsync через IsLoading
            IsLoading = false;
            await LoadQueueAsync();

            StatusMessage = skippedTitles.Count > 0
                ? $"Плейлист «{savedPlaylist.Name}» сохранён ({persistableEntries.Count} песен). Пропущено (не из каталога): {string.Join(", ", skippedTitles)}."
                : $"Плейлист «{savedPlaylist.Name}» успешно сохранён.";
        }
        catch (Exception ex)
        {
            StatusMessage = null;
            await ErrorDialog.ShowAsync("Ошибка при сохранении плейлиста", ex);
        }
        finally
        {
            IsLoading = false;
        }
    }

    private async Task DeleteSavedPlaylistAsync(Playlist? playlist)
    {
        if (playlist is null)
        {
            return;
        }

        var name = playlist.Name;
        var id = playlist.Id;

        try
        {
            StatusMessage = $"Удаляем плейлист «{name}»…";
            await _catalogService.RemovePlaylistAsync(id);

            // Сразу убираем из UI (не через LoadQueueAsync — он блокируется IsLoading)
            var saved = SavedPlaylists.FirstOrDefault(p => p.Id == id);
            if (saved is not null)
            {
                SavedPlaylists.Remove(saved);
            }

            var queueItem = Queue.FirstOrDefault(q => q.PlaylistId == id);
            if (queueItem is not null)
            {
                Queue.Remove(queueItem);
            }

            StatusMessage = $"Плейлист «{name}» удалён.";
        }
        catch (Exception ex)
        {
            StatusMessage = null;
            await ErrorDialog.ShowAsync("Не удалось удалить плейлист", ex);
            // На случай рассинхрона — перезагрузить список
            try
            {
                await LoadQueueAsync();
            }
            catch
            {
                // ignore
            }
        }
    }

    private void LoadPlaylistIntoQuick(Playlist? playlist)
    {
        if (playlist is null)
        {
            return;
        }

        QuickEntries.Clear();
        foreach (var entry in playlist.Entries.OrderBy(e => e.Order))
        {
            QuickEntries.Add(entry);
        }

        _quickPlaylistName = string.IsNullOrWhiteSpace(playlist.Name) ? "Быстрый плейлист" : playlist.Name;

        var quickEntry = new LiveQueueEntry(
            playlist.Id,
            _quickPlaylistName,
            playlist.ScheduledAt,
            "Быстрый плейлист",
            QuickEntries.ToList(),
            playlist.ThemePreset);

        SelectedEntry = quickEntry;
        StatusMessage = $"Плейлист «{playlist.Name}» загружен в быстрый плейлист.";
    }

    private void ToggleBlackout()
    {
        var newState = !_projectionDisplayService.IsBlackout;
        _projectionDisplayService.SetBlackout(newState);
    }

    private void OnProjectionVisibilityChanged(object? sender, bool isOpen)
    {
        IsProjectionWindowOpen = isOpen;
        // Если окно закрывается, сбрасываем флаг показа
        if (!isOpen)
        {
            IsShowStarted = false;
            IsProjectionCleared = false;
        }
        OpenProjectionCommand.NotifyCanExecuteChanged();
        CloseProjectionCommand.NotifyCanExecuteChanged();
        ToggleBlackoutCommand.NotifyCanExecuteChanged();
        ToggleVideoModeCommand.NotifyCanExecuteChanged();
        ToggleNdiVideoModeCommand.NotifyCanExecuteChanged();
        RefreshNdiSourcesCommand.NotifyCanExecuteChanged();
    }

    private void OnBlackoutStateChanged(object? sender, bool isBlackout)
    {
        IsBlackoutEnabled = isBlackout;
    }

    partial void OnSelectedSectionChanged(LiveSectionItem? value)
    {
        InteractionLogger.Log($"OnSelectedSectionChanged called. valueIndex={(value?.Index.ToString() ?? "null")}, _suppressSectionSelection={_suppressSectionSelection}, _isUpdatingSectionsHighlight={_isUpdatingSectionsHighlight}");

        // Игнорируем если изменение SelectedSection произошло программно в UpdateSectionsHighlight
        if (_isUpdatingSectionsHighlight)
        {
            InteractionLogger.Log($"OnSelectedSectionChanged ignored: updating sections highlight programmatically. valueIndex={(value?.Index.ToString() ?? "null")}");
            return;
        }

        if (value is null || _suppressSectionSelection)
        {
            InteractionLogger.Log($"OnSelectedSectionChanged ignored: value is null or suppressed. valueIndex={(value?.Index.ToString() ?? "null")}");
            return;
        }

        var index = Sections.IndexOf(value);
        InteractionLogger.Log($"OnSelectedSectionChanged: clicked index={index}, SectionsCount={Sections.Count}");
        if (index < 0)
        {
            return;
        }

        // После Esc при постоянном фоне state очищен — GoToSection no-op.
        // В этом случае не перезагружаем коллекцию секций (это сбрасывает выделение в UI).
        // Вместо этого просто обновим подсветку локально — пользователь ожидает один клик для выбора.
        if (_projectionStateService.Current.SongId is null
            && SelectedQuickEntry?.Song is not null)
        {
            InteractionLogger.Log($"OnSelectedSectionChanged: projection empty, updating highlight locally to {index}");
            _lastUserSelectionUtc = DateTime.UtcNow;
            UpdateSectionsHighlight(index, forceSetSelected: true);
            StatusMessage = Sections.Count > 0
                ? $"Песня «{SelectedQuickEntry.Song.Title}» выбрана. Загружено {Sections.Count} секций."
                : $"Песня «{SelectedQuickEntry.Song.Title}» выбрана. Секций нет.";
            NotifySectionProgressChanged();
            NotifySongProgressChanged();
            return;
        }

        SkipToSection(index);
    }

    partial void OnIsShowStartedChanged(bool value)
    {
        if (!value && IsBlackoutEnabled)
        {
            IsBlackoutEnabled = false;
        }
        OpenProjectionCommand.NotifyCanExecuteChanged();
        ClearProjectionCommand.NotifyCanExecuteChanged();
        RestoreProjectionCommand.NotifyCanExecuteChanged();
    }

    partial void OnIsProjectionClearedChanged(bool value)
    {
        ClearProjectionCommand.NotifyCanExecuteChanged();
        RestoreProjectionCommand.NotifyCanExecuteChanged();
    }

    partial void OnIsProjectionWindowOpenChanged(bool value)
    {
        if (!value && IsBlackoutEnabled)
        {
            IsBlackoutEnabled = false;
        }
    }

    partial void OnIsBlackoutEnabledChanged(bool value)
    {
        if (_projectionDisplayService.IsBlackout != value)
        {
            _projectionDisplayService.SetBlackout(value);
        }
    }

    private void AdvanceOrNextSong()
    {
        RestoreProjectionIfCleared();
        // Источник истины — состояние проекции; локальный список может быть пуст
        // (например, песня выбрана до инициализации LiveControl).
        var sectionCount = _currentSections.Count > 0
            ? _currentSections.Count
            : Sections.Count;
        var sectionIndex = CurrentState.SectionIndex;

        if (sectionCount == 0)
        {
            // Нет локальных секций — пробуем шагнуть; если уже конец, закрываем
            var before = CurrentState.SectionIndex;
            _projectionStateService.AdvanceSection();
            if (CurrentState.SectionIndex == before && _projectionDisplayService.IsOpen)
            {
                _ = EndShowAsync();
            }

            NotifySectionProgressChanged();
            return;
        }

        if (sectionIndex >= sectionCount - 1)
        {
            // Библия: следующая глава, окно не закрываем
            if (IsBibleProjection(GetCurrentProjectionSong()))
            {
                _ = TryContinueBibleChapterAsync(+1);
                NotifySectionProgressChanged();
                return;
            }

            // Песня: завершаем показ (при постоянном фоне — только текст)
            if (_projectionDisplayService.IsOpen)
            {
                _ = EndShowAsync();
            }

            NotifySectionProgressChanged();
            return;
        }

        _projectionStateService.AdvanceSection();
        NotifySectionProgressChanged();
    }

    private async Task RewindOrPreviousSongAsync()
    {
        RestoreProjectionIfCleared();
        if (_currentSections.Count == 0)
        {
            _projectionStateService.PreviousSection();
            NotifySectionProgressChanged();
            return;
        }

        if (CurrentState.SectionIndex <= 0)
        {
            // Библия: предыдущая глава (с последнего стиха)
            if (IsBibleProjection(GetCurrentProjectionSong()))
            {
                _ = TryContinueBibleChapterAsync(-1);
                NotifySectionProgressChanged();
                return;
            }

            if (_currentEntries.Count > 0)
            {
                await MoveToEntryAsync(CurrentSongIndex - 1, startFromLastSection: true);
            }
            else
            {
                _projectionStateService.PreviousSection();
            }
        }
        else
        {
            _projectionStateService.PreviousSection();
        }
    }

    private void SkipToSection(int index)
    {
        RestoreProjectionIfCleared();
        System.Diagnostics.Debug.WriteLine($"SkipToSection: index={index}, _currentSections.Count={_currentSections.Count}");
        ChyguiSlide.Data.InteractionLogger.Log($"SkipToSection: index={index}, _currentSections.Count={_currentSections.Count}");

        if (_currentSections.Count == 0)
        {
            System.Diagnostics.Debug.WriteLine($"SkipToSection: _currentSections.Count is 0, returning");
            ChyguiSlide.Data.InteractionLogger.Log($"SkipToSection: _currentSections.Count is 0, returning");
            return;
        }

        index = Math.Clamp(index, 0, _currentSections.Count - 1);
        System.Diagnostics.Debug.WriteLine($"SkipToSection: clamped index={index}, CurrentState.SectionIndex={CurrentState.SectionIndex}");
        ChyguiSlide.Data.InteractionLogger.Log($"SkipToSection: clamped index={index}, CurrentState.SectionIndex={CurrentState.SectionIndex}");

        // Убрали проверку index == CurrentState.SectionIndex, потому что
        // UpdateSectionsHighlight может обновить CurrentState.SectionIndex до вызова SkipToSection
        // и тогда GoToSection не будет вызван, хотя содержимое на проекторе нужно обновить

        System.Diagnostics.Debug.WriteLine($"SkipToSection: calling GoToSection({index})");
        ChyguiSlide.Data.InteractionLogger.Log($"SkipToSection: calling GoToSection({index})");
        _projectionStateService.GoToSection(index);
    }

    private async Task MoveToEntryAsync(int targetIndex, bool startFromLastSection = false)
    {
        if (_currentEntries.Count == 0)
        {
            return;
        }

        if (targetIndex < 0 || targetIndex >= _currentEntries.Count)
        {
            StatusMessage = targetIndex < 0
                ? "Это первая песня в сет-листе."
                : "Больше песен в сет-листе нет.";
            NotifySongProgressChanged();
            return;
        }

        var entry = _currentEntries[targetIndex];
        if (entry.Song is null)
        {
            return;
        }

        CurrentSongIndex = targetIndex;
        StatusMessage = $"Песня «{entry.Song.Title}» готова к показу.";

        _currentSections.Clear();
        Sections.Clear();

        var sections = entry.Song.Sections
            .OrderBy(section => section.Order)
            .ToList();

        foreach (var section in sections)
        {
            var snapshot = new SectionSnapshot(section);
            _currentSections.Add(snapshot);
            Sections.Add(new LiveSectionItem(snapshot.Index, snapshot.Title, snapshot.Content, snapshot.Notes));
        }

        var contentSegments = sections
            .Select(section => section.Content ?? string.Empty)
            .ToList();

        var initialIndex = startFromLastSection && _currentSections.Count > 0
            ? _currentSections.Count - 1
            : 0;

        IReadOnlyList<string?>? captions = IsBibleProjection(entry.Song)
            ? sections.Select(section => (string?)section.Heading).ToList()
            : null;

        _projectionStateService.SetSong(
            entry.SongId,
            entry.Song.Title,
            contentSegments,
            initialIndex,
            captions,
            ResolveProjectionContentKind(entry.Song));
        UpdateSectionsHighlight(initialIndex);

        // Не переприменяем тему при смене песни — фон уже на экране
        if (!_projectionDisplayService.IsOpen && _currentThemePreset is not null)
        {
            _projectionDisplayService.ApplyTheme(_currentThemePreset);
        }
        else if (!_projectionDisplayService.IsOpen)
        {
            _ = LoadThemeFromSettingsAsync();
        }
        
        NotifySongProgressChanged();
        NotifySectionProgressChanged();
    }

    private void UpdateSectionsHighlight(int activeIndex, bool forceSetSelected = false)
    {
        InteractionLogger.Log($"UpdateSectionsHighlight called: activeIndex={activeIndex}, SectionsCount={Sections.Count}");

        // Предотвращаем рекурсию при программном изменении SelectedSection
        _isUpdatingSectionsHighlight = true;
        try
        {
            // Если недавно был пользовательский выбор — временно игнорируем внешние апдейты,
            // чтобы не перезаписать выбор при быстро сменяющихся состояниях проекции.
            if (!forceSetSelected && _lastUserSelectionUtc.HasValue)
            {
                var ageMs = (DateTime.UtcNow - _lastUserSelectionUtc.Value).TotalMilliseconds;
                if (ageMs >= 0 && ageMs < 800)
                {
                    InteractionLogger.Log($"UpdateSectionsHighlight skipped due recent user selection (ageMs={ageMs:F0})");
                    return;
                }
                // Сбросим метку старой выборки
                _lastUserSelectionUtc = null;
            }

            // Если на проекторе нет активного слайда (пустой state), не трогаем IsCurrent —
            // это предотвращает мерцание/сброс визуального выделения при простом клике в UI
            var hasActiveContent = _projectionStateService.Current.SongId is not null && _projectionStateService.Current.VisibleLines.Count > 0;

            InteractionLogger.Log($"UpdateSectionsHighlight: hasActiveContent={hasActiveContent}, currentSongId={_projectionStateService.Current.SongId}");

            // Блокируем только если нет активного контента - тогда UpdateSectionsHighlight вызывается программно
            // и нужно предотвратить рекурсию. Если есть активный контент, пользовательские клики должны работать.
            if (!hasActiveContent)
            {
                _suppressSectionSelection = true;
            }

            if (hasActiveContent)
            {
                for (var i = 0; i < Sections.Count; i++)
                {
                    var before = Sections[i].IsCurrent;
                    Sections[i].IsCurrent = i == activeIndex;
                    if (before != Sections[i].IsCurrent)
                    {
                        InteractionLogger.Log($"Section[{i}] IsCurrent changed: {before} -> {Sections[i].IsCurrent}");
                    }
                }
            }

            var prevSelected = SelectedSection?.Index.ToString() ?? "null";
            if (hasActiveContent || forceSetSelected)
            {
                SelectedSection = activeIndex >= 0 && activeIndex < Sections.Count
                    ? Sections[activeIndex]
                    : null;
            }
            var newSelected = SelectedSection?.Index.ToString() ?? "null";
            InteractionLogger.Log($"UpdateSectionsHighlight: SelectedSection changed: {prevSelected} -> {newSelected}");

            if (!hasActiveContent)
            {
                _suppressSectionSelection = false;
            }
        }
        finally
        {
            _isUpdatingSectionsHighlight = false;
        }
    }

    private void NotifySongProgressChanged() => OnPropertyChanged(nameof(SongProgressLabel));

    private void NotifySectionProgressChanged() => OnPropertyChanged(nameof(SectionProgressLabel));

    partial void OnCurrentSongIndexChanged(int value) => NotifySongProgressChanged();

    private async void ToggleVideoMode()
    {
        await _projectionDisplayService.ToggleVideoModeAsync();
    }

    private async void ToggleNdiVideoMode()
    {
        await _projectionDisplayService.ToggleNdiVideoModeAsync();
        // Обновляем состояние после переключения
        await Task.Delay(100); // Небольшая задержка для обновления состояния
        await RefreshNdiSourcesAsync();
        
        // Обновляем IsNdiModeActive из ProjectionDisplayViewModel
        // (через ProjectionDisplayService можно получить доступ к ViewModel, но это не идеально)
        // Пока оставляем как есть - состояние будет обновляться через события
    }

    private async Task RefreshNdiSourcesAsync()
    {
        if (_ndiReceiverService == null)
        {
            return;
        }

        try
        {
            IsLoadingNdiSources = true;
            var sources = await _projectionDisplayService.GetAvailableNdiSourcesAsync();
            
            AvailableNdiSources.Clear();
            foreach (var source in sources)
            {
                AvailableNdiSources.Add(source);
            }

            // Восстанавливаем выбранный источник из настроек
            var savedSourceName = await _displaySettingsService.GetNdiSourceNameAsync();
            if (!string.IsNullOrEmpty(savedSourceName))
            {
                var savedSource = sources.FirstOrDefault(s => s.Name == savedSourceName);
                if (savedSource != null)
                {
                    SelectedNdiSource = savedSource;
                }
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[LiveControlViewModel] Error refreshing NDI sources: {ex.Message}");
        }
        finally
        {
            IsLoadingNdiSources = false;
        }
    }

    partial void OnSelectedNdiSourceChanged(NdiSource? value)
    {
        if (value != null && _ndiReceiverService != null)
        {
            _ = Task.Run(async () =>
            {
                try
                {
                    await _ndiReceiverService.ConnectAsync(value.Name);
                    await _displaySettingsService.SetNdiSourceNameAsync(value.Name);
                    _dispatcher.TryEnqueue(() => IsNdiConnected = _ndiReceiverService.IsConnected);
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"[LiveControlViewModel] Error connecting to NDI source: {ex.Message}");
                    _dispatcher.TryEnqueue(() => IsNdiConnected = false);
                }
            });
        }
    }
}

