using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ChyguiSlide.Services.Abstractions;
using ChyguiSlide.Services.Models;
using Microsoft.Extensions.DependencyInjection;

namespace ChyguiSlide.ViewModels;

public sealed partial class DashboardViewModel : ObservableRecipient
{
    private readonly ICatalogService _catalogService;
    private readonly IServiceProvider _services;
    private bool _suppressFilterReload;

    public DashboardViewModel(ICatalogService catalogService, IServiceProvider services)
    {
        _catalogService = catalogService;
        _services = services;
        CollectionFilters = new ObservableCollection<CollectionFilterOption>();
        TopSongs = new ObservableCollection<TopSongStat>();
        TopLimitOptions = new ObservableCollection<int> { 10, 20, 50, 100 };
        SelectedTopLimit = 20;
        StartSongCommand = new AsyncRelayCommand<TopSongStat>(StartSongAsync, s => s is not null);
        RefreshCommand = new AsyncRelayCommand(LoadAsync);
    }

    public ObservableCollection<CollectionFilterOption> CollectionFilters { get; }

    public ObservableCollection<TopSongStat> TopSongs { get; }

    public ObservableCollection<int> TopLimitOptions { get; }

    [ObservableProperty]
    private CollectionFilterOption? selectedCollectionFilter;

    [ObservableProperty]
    private int selectedTopLimit = 20;

    [ObservableProperty]
    private bool isBusy;

    [ObservableProperty]
    private string? statusMessage;

    public IAsyncRelayCommand RefreshCommand { get; }

    public IAsyncRelayCommand<TopSongStat> StartSongCommand { get; }

    partial void OnSelectedCollectionFilterChanged(CollectionFilterOption? value)
    {
        if (_suppressFilterReload)
        {
            return;
        }

        _ = LoadTopSongsAsync();
    }

    partial void OnSelectedTopLimitChanged(int value)
    {
        if (_suppressFilterReload)
        {
            return;
        }

        _ = LoadTopSongsAsync();
    }

    public async Task InitializeAsync()
    {
        await LoadAsync();
    }

    private async Task LoadAsync()
    {
        if (IsBusy)
        {
            return;
        }

        try
        {
            IsBusy = true;
            await RefreshCollectionFiltersAsync();
            await LoadTopSongsAsync();
        }
        finally
        {
            IsBusy = false;
        }
    }

    private async Task RefreshCollectionFiltersAsync()
    {
        var collections = await _catalogService.GetSongCollectionsAsync();
        var previousId = SelectedCollectionFilter?.CollectionId;
        var previousKind = SelectedCollectionFilter?.Kind ?? CollectionFilterKind.All;

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

            SelectedCollectionFilter = previousKind == CollectionFilterKind.Specific && previousId is Guid id
                ? CollectionFilters.FirstOrDefault(f => f.CollectionId == id) ?? CollectionFilterOption.All
                : CollectionFilterOption.All;
        }
        finally
        {
            _suppressFilterReload = false;
        }
    }

    private async Task LoadTopSongsAsync()
    {
        var collectionId = SelectedCollectionFilter?.Kind == CollectionFilterKind.Specific
            ? SelectedCollectionFilter.CollectionId
            : null;

        var take = SelectedTopLimit is 10 or 20 or 50 or 100 ? SelectedTopLimit : 20;
        var stats = await _catalogService.GetTopSongsAsync(take, collectionId);
        TopSongs.Clear();
        var rank = 1;
        foreach (var item in stats)
        {
            TopSongs.Add(new TopSongStat
            {
                SongId = item.SongId,
                Title = item.Title,
                Number = item.Number,
                CollectionId = item.CollectionId,
                CollectionName = item.CollectionName,
                PlayCount = item.PlayCount,
                LastPlayedAt = item.LastPlayedAt,
                Rank = rank++
            });
        }

        StatusMessage = TopSongs.Count == 0
            ? "Пока нет показов — топ появится после трансляции песен."
            : $"Топ {TopSongs.Count} по числу показов";
    }

    private async Task StartSongAsync(TopSongStat? stat)
    {
        if (stat is null)
        {
            return;
        }

        var song = await _catalogService.GetSongAsync(stat.SongId);
        if (song is null)
        {
            StatusMessage = "Песня не найдена.";
            return;
        }

        var live = _services.GetRequiredService<LiveControlViewModel>();
        await live.StartSongFromCatalogAsync(song);
        await LoadTopSongsAsync();
    }
}
