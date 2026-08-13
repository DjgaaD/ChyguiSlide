using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ChyguiSlide.Data.Entities;
using ChyguiSlide.Data.Enums;
using ChyguiSlide.Services.Abstractions;
using Microsoft.Extensions.DependencyInjection;
using ChyguiSlide.Views.Dialogs;

namespace ChyguiSlide.ViewModels;

public partial class AnnouncementsViewModel : ObservableObject
{
    private readonly IServiceProvider _services;
    private IReadOnlyList<Announcement> _allItems = Array.Empty<Announcement>();
    private bool _quickPlaylistHooked;

    public AnnouncementsViewModel(IServiceProvider services)
    {
        _services = services;

        Items = new ObservableCollection<Announcement>();
        SlidesPreview = new ObservableCollection<AnnouncementSlidePreviewItem>();

        LoadCommand = new AsyncRelayCommand(LoadAsync);
        SearchCommand = new AsyncRelayCommand<string?>(SearchAsync);
        StartProjectionCommand = new AsyncRelayCommand(StartProjectionAsync, () => SelectedAnnouncement is not null);
        TogglePinCommand = new AsyncRelayCommand(TogglePinAsync, () => SelectedAnnouncement is not null);
        AddToQuickPlaylistCommand = new RelayCommand(AddToQuickPlaylist, () => SelectedAnnouncement is not null);
    }

    public ObservableCollection<Announcement> Items { get; }

    public ObservableCollection<AnnouncementSlidePreviewItem> SlidesPreview { get; }

    [ObservableProperty]
    private bool isBusy;

    [ObservableProperty]
    private string? statusMessage;

    [ObservableProperty]
    private string? searchTerm;

    [ObservableProperty]
    private Announcement? selectedAnnouncement;

    [ObservableProperty]
    private AnnouncementSlidePreviewItem? selectedSlidePreview;

    [ObservableProperty]
    private bool isSelectedInQuickPlaylist;

    public bool HasSelectedAnnouncement => SelectedAnnouncement is not null;

    public bool ShowAddToQuickPlaylistButton =>
        HasSelectedAnnouncement && !IsSelectedInQuickPlaylist;

    public bool ShowInQuickPlaylistBadge =>
        HasSelectedAnnouncement && IsSelectedInQuickPlaylist;

    public IAsyncRelayCommand LoadCommand { get; }
    public IAsyncRelayCommand<string?> SearchCommand { get; }
    public IAsyncRelayCommand StartProjectionCommand { get; }
    public IAsyncRelayCommand TogglePinCommand { get; }
    public IRelayCommand AddToQuickPlaylistCommand { get; }

    public async Task InitializeAsync()
    {
        EnsureQuickPlaylistHook();
        await LoadAsync();
    }

    partial void OnSelectedAnnouncementChanged(Announcement? value)
    {
        LoadSlidesPreview(value);
        OnPropertyChanged(nameof(HasSelectedAnnouncement));
        OnPropertyChanged(nameof(ShowAddToQuickPlaylistButton));
        OnPropertyChanged(nameof(ShowInQuickPlaylistBadge));
        StartProjectionCommand.NotifyCanExecuteChanged();
        TogglePinCommand.NotifyCanExecuteChanged();
        AddToQuickPlaylistCommand.NotifyCanExecuteChanged();
        RefreshQuickPlaylistMembership();
    }

    partial void OnIsSelectedInQuickPlaylistChanged(bool value)
    {
        OnPropertyChanged(nameof(ShowAddToQuickPlaylistButton));
        OnPropertyChanged(nameof(ShowInQuickPlaylistBadge));
    }

    partial void OnSelectedSlidePreviewChanged(AnnouncementSlidePreviewItem? value)
    {
        foreach (var slide in SlidesPreview)
        {
            slide.IsSelected = ReferenceEquals(slide, value);
        }
    }

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

    private void RefreshQuickPlaylistMembership()
    {
        if (SelectedAnnouncement is null)
        {
            IsSelectedInQuickPlaylist = false;
            return;
        }

        var live = _services.GetRequiredService<LiveControlViewModel>();
        IsSelectedInQuickPlaylist = live.IsSongInQuickPlaylist(SelectedAnnouncement.Id);
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
            using var scope = _services.CreateScope();
            _allItems = await CreateService(scope).GetPermanentAsync();
            ApplyFilter();
            StatusMessage = Items.Count > 0
                ? $"Объявлений: {Items.Count}"
                : "Пока нет сохранённых объявлений";
        }
        catch (Exception ex)
        {
            StatusMessage = null;
            await ErrorDialog.ShowAsync("Ошибка загрузки объявлений", ex);
        }
        finally
        {
            IsBusy = false;
        }
    }

    private Task SearchAsync(string? query)
    {
        SearchTerm = query;
        ApplyFilter();
        return Task.CompletedTask;
    }

    private void ApplyFilter()
    {
        var selectedId = SelectedAnnouncement?.Id;
        IEnumerable<Announcement> query = _allItems;

        if (!string.IsNullOrWhiteSpace(SearchTerm))
        {
            var term = SearchTerm.Trim();
            query = query.Where(a =>
                (a.Title?.Contains(term, StringComparison.CurrentCultureIgnoreCase) ?? false) ||
                (a.Content?.Contains(term, StringComparison.CurrentCultureIgnoreCase) ?? false));
        }

        var list = query
            .OrderByDescending(a => a.IsPinned)
            .ThenBy(a => a.SortOrder)
            .ThenByDescending(a => a.UpdatedAt)
            .ToList();

        Items.Clear();
        foreach (var item in list)
        {
            Items.Add(item);
        }

        SelectedAnnouncement = selectedId is Guid id
            ? Items.FirstOrDefault(a => a.Id == id)
            : Items.FirstOrDefault();
    }

    private void LoadSlidesPreview(Announcement? announcement)
    {
        SlidesPreview.Clear();
        SelectedSlidePreview = null;

        if (announcement is null)
        {
            return;
        }

        var song = BuildEphemeralSong(announcement);
        var sections = song.Sections?.OrderBy(s => s.Order).ToList() ?? new List<SongSection>();
        for (var i = 0; i < sections.Count; i++)
        {
            var section = sections[i];
            SlidesPreview.Add(new AnnouncementSlidePreviewItem(
                i,
                section.Heading ?? $"Слайд {i + 1}",
                string.IsNullOrWhiteSpace(section.Content) ? string.Empty : section.Content.Trim()));
        }

        SelectedSlidePreview = SlidesPreview.FirstOrDefault();
    }

    private async Task StartProjectionAsync()
    {
        if (SelectedAnnouncement is null)
        {
            return;
        }

        var live = _services.GetRequiredService<LiveControlViewModel>();
        var song = BuildEphemeralSong(SelectedAnnouncement);
        var startIndex = SelectedSlidePreview?.ListIndex ?? 0;
        await live.StartSongFromCatalogAsync(song, startIndex);
        StatusMessage = $"На экране: «{song.Title}»";
    }

    private void AddToQuickPlaylist()
    {
        if (SelectedAnnouncement is null)
        {
            return;
        }

        var live = _services.GetRequiredService<LiveControlViewModel>();
        live.AddSongToQuickPlaylist(BuildEphemeralSong(SelectedAnnouncement));
        RefreshQuickPlaylistMembership();
    }

    public async Task TogglePinAsync()
    {
        if (SelectedAnnouncement is null)
        {
            return;
        }

        try
        {
            using var scope = _services.CreateScope();
            var service = CreateService(scope);
            var entity = new Announcement
            {
                Id = SelectedAnnouncement.Id,
                Title = SelectedAnnouncement.Title,
                Content = SelectedAnnouncement.Content,
                IsPinned = !SelectedAnnouncement.IsPinned,
                IsPermanent = true,
                SortOrder = SelectedAnnouncement.SortOrder
            };

            await service.SaveAsync(entity);
            await LoadAsync();
            StatusMessage = entity.IsPinned ? "Объявление закреплено" : "Закрепление снято";
        }
        catch (Exception ex)
        {
            await ErrorDialog.ShowAsync("Ошибка", ex);
        }
    }

    public async Task DeleteAsync(Announcement announcement)
    {
        try
        {
            using var scope = _services.CreateScope();
            await CreateService(scope).DeleteAsync(announcement.Id);
            if (SelectedAnnouncement?.Id == announcement.Id)
            {
                SelectedAnnouncement = null;
            }

            await LoadAsync();
            StatusMessage = $"Удалено: «{announcement.Title}»";
        }
        catch (Exception ex)
        {
            await ErrorDialog.ShowAsync("Ошибка удаления", ex);
        }
    }

    public void SelectAnnouncement(Guid id)
    {
        SelectedAnnouncement = Items.FirstOrDefault(a => a.Id == id)
                               ?? _allItems.FirstOrDefault(a => a.Id == id);
    }

    public async Task StartProjectionFromHotkeyAsync()
    {
        if (SelectedAnnouncement is not null)
        {
            await StartProjectionAsync();
            return;
        }

        if (!string.IsNullOrWhiteSpace(_lastQuickContent))
        {
            await ShowQuickAsync(_lastQuickContent);
        }
    }

    private string _lastQuickContent = string.Empty;

    public async Task ShowQuickAsync(string content)
    {
        if (string.IsNullOrWhiteSpace(content))
        {
            StatusMessage = "Введите текст объявления";
            return;
        }

        _lastQuickContent = content.Trim();

        var live = _services.GetRequiredService<LiveControlViewModel>();
        var song = BuildEphemeralSong(Guid.NewGuid(), "Объявление", _lastQuickContent);
        await live.StartSongFromCatalogAsync(song);
        StatusMessage = "Быстрое объявление на экране";
    }

    private IAnnouncementService CreateService(IServiceScope scope) =>
        scope.ServiceProvider.GetRequiredService<IAnnouncementService>();

    internal static Song BuildEphemeralSong(Announcement announcement)
    {
        var title = string.IsNullOrWhiteSpace(announcement.Title)
            ? "Объявление"
            : announcement.Title.Trim();

        return BuildEphemeralSong(announcement.Id, title, announcement.Content);
    }

    internal static Song BuildEphemeralSong(Guid songId, string title, string content)
    {
        var slides = SplitIntoSlides(content);
        if (slides.Count == 0)
        {
            slides.Add(content.Trim());
        }

        var sections = slides
            .Select((slide, index) => new SongSection
            {
                Id = Guid.NewGuid(),
                SongId = songId,
                Order = index,
                SectionType = SectionType.Custom,
                Heading = slides.Count == 1 ? title : $"{title} ({index + 1}/{slides.Count})",
                Content = slide
            })
            .ToList();

        return new Song
        {
            Id = songId,
            Title = title,
            Subtitle = "Объявление",
            Language = "ru",
            Sections = sections
        };
    }

    private static List<string> SplitIntoSlides(string content)
    {
        return content
            .Replace("\r\n", "\n")
            .Split(new[] { "\n\n" }, StringSplitOptions.None)
            .Select(p => p.Trim())
            .Where(p => p.Length > 0)
            .ToList();
    }
}
