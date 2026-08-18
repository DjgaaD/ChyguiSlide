using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ChyguiSlide.Data.Entities;
using ChyguiSlide.Services;
using ChyguiSlide.Services.Abstractions;
using ChyguiSlide.Services.Implementations;
using ChyguiSlide.Services.Models;
using Microsoft.Extensions.DependencyInjection;

namespace ChyguiSlide.ViewModels;

public partial class CatalogViewModel : ObservableRecipient
{
    private readonly ICatalogService _catalogService;
    private readonly IDisplaySettingsService _displaySettings;
    private readonly IServiceProvider _services;
    private CatalogSortMode _sortMode = CatalogSortMode.Title;
    private bool _settingsLoaded;
    private bool _suppressFilterReload;
    private bool _pendingSearchFocusRequest;

    public CatalogViewModel(
        ICatalogService catalogService,
        IDisplaySettingsService displaySettings,
        IServiceProvider services)
    {
        _catalogService = catalogService;
        _displaySettings = displaySettings;
        _services = services;
        Songs = new ObservableCollection<Song>();
        SectionsPreview = new ObservableCollection<CatalogSectionPreviewItem>();
        CollectionFilters = new ObservableCollection<CollectionFilterOption>();

        LoadSongsCommand = new AsyncRelayCommand(LoadSongsAsync);
        SearchCommand = new AsyncRelayCommand<string?>(SearchAsync);
        StartProjectionCommand = new AsyncRelayCommand(StartProjectionAsync, () => SelectedSong is not null);
        SortByTitleCommand = new AsyncRelayCommand(SortByTitleAsync);
        SortByNumberCommand = new AsyncRelayCommand(SortByNumberAsync);
        DeleteSongCommand = new AsyncRelayCommand(DeleteSongAsync, () => SelectedSong is not null);
        AddToQuickPlaylistCommand = new RelayCommand(AddToQuickPlaylist, () => SelectedSong is not null);
        CreateCollectionCommand = new AsyncRelayCommand<string?>(CreateCollectionAsync);
        DeleteSelectedCollectionCommand = new AsyncRelayCommand(DeleteSelectedCollectionAsync, CanDeleteSelectedCollection);
        ExpandChorusInCollectionCommand = new AsyncRelayCommand(
            async () => { await ExpandChorusInSelectedCollectionAsync(); },
            CanDeleteSelectedCollection);
    }

    public ObservableCollection<Song> Songs { get; }

    public ObservableCollection<CatalogSectionPreviewItem> SectionsPreview { get; }

    public ObservableCollection<CollectionFilterOption> CollectionFilters { get; }
    public event Action? SearchFocusRequested;

    [ObservableProperty]
    private CollectionFilterOption? selectedCollectionFilter;

    [ObservableProperty]
    private Song? selectedSong;

    [ObservableProperty]
    private CatalogSectionPreviewItem? selectedSectionPreview;

    partial void OnSelectedSongChanged(Song? value)
    {
        _ = LoadSectionsPreviewAsync(value);

        OnPropertyChanged(nameof(HasSelectedSong));
        OnPropertyChanged(nameof(ShowAddToQuickPlaylistButton));
        OnPropertyChanged(nameof(ShowInQuickPlaylistBadge));
        StartProjectionCommand.NotifyCanExecuteChanged();
        DeleteSongCommand.NotifyCanExecuteChanged();
        AddToQuickPlaylistCommand.NotifyCanExecuteChanged();
        RefreshQuickPlaylistMembership();
    }

    [ObservableProperty]
    private bool isSelectedInQuickPlaylist;

    partial void OnIsSelectedInQuickPlaylistChanged(bool value)
    {
        OnPropertyChanged(nameof(ShowAddToQuickPlaylistButton));
        OnPropertyChanged(nameof(ShowInQuickPlaylistBadge));
    }

    private void RefreshQuickPlaylistMembership()
    {
        if (SelectedSong is null)
        {
            IsSelectedInQuickPlaylist = false;
            return;
        }

        var live = _services.GetRequiredService<LiveControlViewModel>();
        IsSelectedInQuickPlaylist = live.IsSongInQuickPlaylist(SelectedSong.Id);
    }

    private bool _quickPlaylistHooked;

    private void EnsureQuickPlaylistHook()
    {
        if (_quickPlaylistHooked)
        {
            return;
        }

        var live = _services.GetRequiredService<LiveControlViewModel>();
        live.QuickEntries.CollectionChanged += (_, _) => RefreshQuickPlaylistMembership();
        _quickPlaylistHooked = true;
    }

    partial void OnSelectedSectionPreviewChanged(CatalogSectionPreviewItem? value)
    {
        foreach (var section in SectionsPreview)
        {
            section.IsSelected = ReferenceEquals(section, value);
        }
    }

    private async Task LoadSectionsPreviewAsync(Song? song)
    {
        SectionsPreview.Clear();
        SelectedSectionPreview = null;

        if (song is null)
        {
            return;
        }

        Song fullSong = song;
        if (song.Sections is null || song.Sections.Count == 0)
        {
            fullSong = await _catalogService.GetSongAsync(song.Id) ?? song;
            if (ReferenceEquals(SelectedSong, song) && fullSong.Sections is { Count: > 0 })
            {
                song.Sections = fullSong.Sections;
            }
        }

        if (!ReferenceEquals(SelectedSong, song) && SelectedSong?.Id != song.Id)
        {
            return;
        }

        var ordered = fullSong.Sections?
            .OrderBy(section => section.Order)
            .ToList() ?? new List<SongSection>();

        for (var i = 0; i < ordered.Count; i++)
        {
            SectionsPreview.Add(new CatalogSectionPreviewItem(i, ordered[i]));
        }

        SelectedSectionPreview = SectionsPreview.FirstOrDefault();
    }

    partial void OnSelectedCollectionFilterChanged(CollectionFilterOption? value)
    {
        OnPropertyChanged(nameof(IsSpecificCollectionSelected));
        OnPropertyChanged(nameof(IsAllSongsFilter));
        DeleteSelectedCollectionCommand.NotifyCanExecuteChanged();
        ExpandChorusInCollectionCommand.NotifyCanExecuteChanged();
        if (_suppressFilterReload)
        {
            return;
        }

        _ = ReloadForFilterAsync();
    }

    /// <summary>Выбран конкретный сборник (не «Все песни»).</summary>
    public bool IsSpecificCollectionSelected =>
        SelectedCollectionFilter?.Kind == CollectionFilterKind.Specific
        && SelectedCollectionFilter.CollectionId is not null;

    /// <summary>Фильтр «Все песни» — создание сборника и импорт SPS.</summary>
    public bool IsAllSongsFilter => !IsSpecificCollectionSelected;

    public bool HasSelectedSong => SelectedSong is not null;

    /// <summary>Показать кнопку «В плейлист».</summary>
    public bool ShowAddToQuickPlaylistButton =>
        HasSelectedSong && !IsSelectedInQuickPlaylist;

    /// <summary>Песня уже в быстром плейлисте — бейдж «В плейлисте».</summary>
    public bool ShowInQuickPlaylistBadge =>
        HasSelectedSong && IsSelectedInQuickPlaylist;

    [ObservableProperty]
    private bool isBusy;

    [ObservableProperty]
    private string? searchTerm;

    public IAsyncRelayCommand LoadSongsCommand { get; }

    public IAsyncRelayCommand<string?> SearchCommand { get; }

    public IAsyncRelayCommand StartProjectionCommand { get; }

    public IAsyncRelayCommand SortByTitleCommand { get; }

    public IAsyncRelayCommand SortByNumberCommand { get; }

    public IAsyncRelayCommand DeleteSongCommand { get; }

    public IRelayCommand AddToQuickPlaylistCommand { get; }

    public IAsyncRelayCommand<string?> CreateCollectionCommand { get; }

    public IAsyncRelayCommand DeleteSelectedCollectionCommand { get; }

    public IAsyncRelayCommand ExpandChorusInCollectionCommand { get; }

    public Guid? ActiveCollectionIdForNewSong =>
        SelectedCollectionFilter?.Kind == CollectionFilterKind.Specific
            ? SelectedCollectionFilter.CollectionId
            : null;

    public async Task InitializeAsync()
    {
        EnsureQuickPlaylistHook();

        if (!_settingsLoaded)
        {
            _sortMode = await _displaySettings.GetCatalogSortModeAsync();
            _settingsLoaded = true;
        }

        await RefreshCollectionFiltersAsync();

        if (Songs.Count == 0)
        {
            await LoadSongsCommand.ExecuteAsync(null);
        }

        RefreshQuickPlaylistMembership();
    }

    public void RequestSearchFocus()
    {
        _pendingSearchFocusRequest = true;
        SearchFocusRequested?.Invoke();
    }

    public bool ConsumePendingSearchFocusRequest()
    {
        if (!_pendingSearchFocusRequest)
        {
            return false;
        }

        _pendingSearchFocusRequest = false;
        return true;
    }

    public async Task RefreshCollectionFiltersAsync(Guid? preferCollectionId = null)
    {
        var collections = await _catalogService.GetSongCollectionsAsync();
        var previousKind = SelectedCollectionFilter?.Kind ?? CollectionFilterKind.All;
        var previousId = SelectedCollectionFilter?.CollectionId;

        _suppressFilterReload = true;
        try
        {
            CollectionFilters.Clear();
            CollectionFilters.Add(CollectionFilterOption.All);
            foreach (var collection in collections)
            {
                CollectionFilters.Add(new CollectionFilterOption(
                    collection.Name,
                    CollectionFilterKind.Specific,
                    collection));
            }

            CollectionFilterOption? next = null;
            if (preferCollectionId is Guid preferred)
            {
                next = CollectionFilters.FirstOrDefault(f =>
                    f.Kind == CollectionFilterKind.Specific && f.CollectionId == preferred);
            }
            else if (previousKind == CollectionFilterKind.Specific && previousId is Guid id)
            {
                next = CollectionFilters.FirstOrDefault(f =>
                    f.Kind == CollectionFilterKind.Specific && f.CollectionId == id);
            }

            SelectedCollectionFilter = next ?? CollectionFilterOption.All;
        }
        finally
        {
            _suppressFilterReload = false;
        }
    }

    private async Task LoadSongsAsync()
    {
        await ReloadForFilterAsync();
    }

    private async Task SearchAsync(string? query)
    {
        SearchTerm = query;
        await ReloadForFilterAsync();
    }

    private async Task ReloadForFilterAsync()
    {
        var filter = SelectedCollectionFilter ?? CollectionFilterOption.All;
        var query = SearchTerm;

        Func<Task<IReadOnlyList<Song>>> loader = filter.Kind switch
        {
            CollectionFilterKind.Specific => () => _catalogService.GetSongsByCollectionAsync(filter.CollectionId),
            _ => () => _catalogService.GetSongsAsync()
        };

        await LoadInternalAsync(async () =>
        {
            var songs = await loader();
            if (!string.IsNullOrWhiteSpace(query))
            {
                songs = await FilterBySearchAsync(songs, query);
            }

            return songs;
        });
    }

    private async Task<IReadOnlyList<Song>> FilterBySearchAsync(IReadOnlyList<Song> source, string query)
    {
        // Полный поиск (название, куплеты и т.д.) через сервис, затем сужение по сборнику.
        var found = await _catalogService.SearchSongsAsync(query);
        if ((SelectedCollectionFilter?.Kind ?? CollectionFilterKind.All) == CollectionFilterKind.All)
        {
            return found;
        }

        var collectionId = SelectedCollectionFilter?.CollectionId;
        return found
            .Where(song => song.CollectionId == collectionId)
            .ToList();
    }

    private async Task StartProjectionAsync()
    {
        if (SelectedSong is null)
        {
            return;
        }

        var live = _services.GetRequiredService<LiveControlViewModel>();
        var startIndex = SelectedSectionPreview?.ListIndex ?? 0;
        await live.StartSongFromCatalogAsync(SelectedSong, startIndex);
    }

    private async Task LoadInternalAsync(Func<Task<IReadOnlyList<Song>>> loader)
    {
        if (IsBusy)
        {
            return;
        }

        try
        {
            IsBusy = true;
            var result = await loader().ConfigureAwait(false);

            Songs.Clear();
            foreach (var song in result)
            {
                Songs.Add(song);
            }

            ApplySortInPlace();

            if (Songs.Count > 0)
            {
                SelectedSong ??= Songs.First();
            }
            else
            {
                SelectedSong = null;
            }
        }
        finally
        {
            IsBusy = false;
        }
    }

    private async Task SortByTitleAsync()
    {
        _sortMode = CatalogSortMode.Title;
        ApplySortInPlace();
        await _displaySettings.SetCatalogSortModeAsync(_sortMode);
    }

    private async Task SortByNumberAsync()
    {
        _sortMode = CatalogSortMode.Number;
        ApplySortInPlace();
        await _displaySettings.SetCatalogSortModeAsync(_sortMode);
    }

    private void ApplySortInPlace()
    {
        var sorted = _sortMode == CatalogSortMode.Number
            ? Songs.OrderBy(s => s.Number ?? int.MaxValue)
                .ThenBy(s => s.Title, StringComparer.CurrentCultureIgnoreCase)
                .ToList()
            : Songs.OrderBy(s => s.Title, StringComparer.CurrentCultureIgnoreCase).ToList();

        var selectedId = SelectedSong?.Id;
        Songs.Clear();
        foreach (var song in sorted)
        {
            Songs.Add(song);
        }

        if (selectedId is Guid id)
        {
            SelectedSong = Songs.FirstOrDefault(s => s.Id == id) ?? SelectedSong;
        }
    }

    private async Task DeleteSongAsync()
    {
        if (SelectedSong is null)
        {
            return;
        }

        var songId = SelectedSong.Id;
        await _catalogService.RemoveSongAsync(songId);
        await ReloadForFilterAsync();
    }

    private void AddToQuickPlaylist()
    {
        if (SelectedSong is null)
        {
            return;
        }

        var live = _services.GetRequiredService<LiveControlViewModel>();
        live.AddSongToQuickPlaylist(SelectedSong);
        RefreshQuickPlaylistMembership();
    }

    private async Task CreateCollectionAsync(string? name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return;
        }

        var collection = await _catalogService.UpsertSongCollectionAsync(new SongCollection
        {
            Name = name.Trim()
        });

        await RefreshCollectionFiltersAsync(collection.Id);
        await ReloadForFilterAsync();
    }

    private bool CanDeleteSelectedCollection() =>
        SelectedCollectionFilter?.Kind == CollectionFilterKind.Specific
        && SelectedCollectionFilter.CollectionId is not null;

    private async Task DeleteSelectedCollectionAsync()
    {
        if (!CanDeleteSelectedCollection() || SelectedCollectionFilter?.CollectionId is not Guid id)
        {
            return;
        }

        await _catalogService.RemoveSongCollectionAsync(id);
        await RefreshCollectionFiltersAsync();
        await ReloadForFilterAsync();
    }

    /// <summary>
    /// Для всех песен выбранного сборника: после каждого куплета вставить припев (идемпотентно).
    /// </summary>
    public async Task<(int SongsChanged, int ChorusesInserted)> ExpandChorusInSelectedCollectionAsync(
        IProgress<SpsImportProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        if (!CanDeleteSelectedCollection() || SelectedCollectionFilter?.CollectionId is not Guid collectionId)
        {
            return (0, 0);
        }

        IsBusy = true;
        try
        {
            var songs = await _catalogService.GetSongsByCollectionAsync(collectionId, cancellationToken);
            var songsChanged = 0;
            var chorusesInserted = 0;
            var total = songs.Count;

            for (var i = 0; i < songs.Count; i++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var song = songs[i];
                song.Collection = null;
                var sections = song.Sections?.OrderBy(s => s.Order).ToList() ?? new List<SongSection>();
                foreach (var section in sections)
                {
                    section.Song = null!;
                }

                var inserted = SongChorusLayout.ExpandChorusAfterEachVerse(sections);
                if (inserted > 0)
                {
                    song.Sections = sections;
                    song.SongTags = null!;
                    await _catalogService.UpsertSongAsync(song, cancellationToken);
                    songsChanged++;
                    chorusesInserted += inserted;
                }

                if (i % 20 == 0 || i == total - 1)
                {
                    progress?.Report(new SpsImportProgress(
                        i + 1,
                        total,
                        $"Припев после куплетов: {i + 1} / {total}"));
                }
            }

            await ReloadForFilterAsync();
            return (songsChanged, chorusesInserted);
        }
        finally
        {
            IsBusy = false;
        }
    }

    /// <summary>Импорт сборника SoftProjector (.sps). Дубликаты по номеру в сборнике пропускаются.</summary>
    public async Task<SpsImportSummary> ImportSpsAsync(
        string filePath,
        IProgress<SpsImportProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(filePath);

        IsBusy = true;
        try
        {
            var importer = _services.GetRequiredService<SoftProjectorSpsImportService>();
            progress?.Report(new SpsImportProgress(0, 0, "Чтение файла…"));

            var parsed = await importer.ImportFromFileAsync(filePath, cancellationToken: cancellationToken);
            if (parsed.Songs.Count == 0)
            {
                return new SpsImportSummary(parsed.SongbookName, 0, 0, parsed.Warning ?? "В файле нет песен.");
            }

            var collections = await _catalogService.GetSongCollectionsAsync(cancellationToken);
            var collection = collections.FirstOrDefault(c =>
                c.Name.Equals(parsed.SongbookName, StringComparison.OrdinalIgnoreCase));

            if (collection is null)
            {
                collection = await _catalogService.UpsertSongCollectionAsync(new SongCollection
                {
                    Name = parsed.SongbookName,
                    Description = parsed.Description,
                    SortOrder = collections.Count + 1
                }, cancellationToken);
            }

            var existing = await _catalogService.GetSongsByCollectionAsync(collection.Id, cancellationToken);
            var existingByNumber = existing
                .Where(s => s.Number is not null)
                .GroupBy(s => s.Number!.Value)
                .ToDictionary(g => g.Key, g => g.First());

            var imported = 0;
            var skipped = 0;
            var repaired = 0;
            var failed = 0;
            string? firstFailure = null;
            var total = parsed.Songs.Count;

            for (var i = 0; i < parsed.Songs.Count; i++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var song = parsed.Songs[i];
                song.CollectionId = collection.Id;

                if (song.Number is int number && existingByNumber.TryGetValue(number, out var existingSong))
                {
                    var hasText = existingSong.Sections?.Any(s => !string.IsNullOrWhiteSpace(s.Content)) == true;
                    if (hasText)
                    {
                        skipped++;
                    }
                    else
                    {
                        // Битый прошлый импорт (песня без текста) — перезаписать
                        try
                        {
                            song.Id = existingSong.Id;
                            song.SongTags = null!;
                            song.Collection = null;
                            await _catalogService.UpsertSongAsync(song, cancellationToken);
                            repaired++;
                            imported++;
                        }
                        catch (Exception ex)
                        {
                            failed++;
                            firstFailure ??= FormatException(ex);
                        }
                    }
                }
                else
                {
                    try
                    {
                        await _catalogService.UpsertSongAsync(song, cancellationToken);
                        if (song.Number is int n)
                        {
                            existingByNumber[n] = song;
                        }

                        imported++;
                    }
                    catch (Exception ex)
                    {
                        failed++;
                        firstFailure ??= FormatException(ex);
                        System.Diagnostics.Debug.WriteLine(
                            $"[SpsImport] Fail #{song.Number} {song.Title}: {FormatException(ex)}");
                    }
                }

                if (i % 10 == 0 || i == total - 1)
                {
                    progress?.Report(new SpsImportProgress(
                        i + 1,
                        total,
                        $"Импорт «{parsed.SongbookName}»: {i + 1} / {total}"));
                }
            }

            await RefreshCollectionFiltersAsync(collection.Id);
            await ReloadForFilterAsync();

            var warning = parsed.Warning;
            if (skipped > 0)
            {
                var skipMsg = $"Пропущено уже существующих: {skipped}.";
                warning = string.IsNullOrWhiteSpace(warning) ? skipMsg : $"{warning} {skipMsg}";
            }

            if (repaired > 0)
            {
                var repairMsg = $"Восстановлено без текста: {repaired}.";
                warning = string.IsNullOrWhiteSpace(warning) ? repairMsg : $"{warning} {repairMsg}";
            }

            if (failed > 0)
            {
                var failMsg = $"Ошибок при сохранении: {failed}.";
                if (!string.IsNullOrWhiteSpace(firstFailure))
                {
                    failMsg += $" Пример: {firstFailure}";
                }

                warning = string.IsNullOrWhiteSpace(warning) ? failMsg : $"{warning} {failMsg}";
            }

            return new SpsImportSummary(parsed.SongbookName, imported, skipped, warning);
        }
        finally
        {
            IsBusy = false;
        }
    }

    private static string FormatException(Exception ex)
    {
        var current = ex;
        while (current.InnerException is not null)
        {
            current = current.InnerException;
        }

        return current.Message;
    }
}

public readonly record struct SpsImportProgress(int Done, int Total, string Message);

public readonly record struct SpsImportSummary(string SongbookName, int Imported, int Skipped, string? Warning);
