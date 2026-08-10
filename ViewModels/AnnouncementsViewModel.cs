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

    public AnnouncementsViewModel(IServiceProvider services)
    {
        _services = services;

        PermanentItems = new ObservableCollection<Announcement>();

        LoadCommand = new AsyncRelayCommand(LoadAsync);
        ShowQuickCommand = new AsyncRelayCommand(ShowQuickAsync, CanShowQuick);
        SaveQuickCommand = new AsyncRelayCommand(SaveQuickAsync, CanShowQuick);
        ShowSelectedCommand = new AsyncRelayCommand(ShowSelectedAsync, CanShowEditor);
        SaveSelectedCommand = new AsyncRelayCommand(SaveSelectedAsync, () => !string.IsNullOrWhiteSpace(EditContent));
        DeleteSelectedCommand = new AsyncRelayCommand(DeleteSelectedAsync, () => SelectedPermanent is not null);
        NewPermanentCommand = new RelayCommand(StartNewPermanent);
        TogglePinCommand = new AsyncRelayCommand(TogglePinAsync, () => SelectedPermanent is not null);
    }

    public ObservableCollection<Announcement> PermanentItems { get; }

    [ObservableProperty]
    private bool isBusy;

    [ObservableProperty]
    private string? statusMessage;

    [ObservableProperty]
    private string quickTitle = string.Empty;

    [ObservableProperty]
    private string quickContent = string.Empty;

    [ObservableProperty]
    private Announcement? selectedPermanent;

    [ObservableProperty]
    private string editTitle = string.Empty;

    [ObservableProperty]
    private string editContent = string.Empty;

    [ObservableProperty]
    private bool editIsPinned;

    public IAsyncRelayCommand LoadCommand { get; }
    public IAsyncRelayCommand ShowQuickCommand { get; }
    public IAsyncRelayCommand SaveQuickCommand { get; }
    public IAsyncRelayCommand ShowSelectedCommand { get; }
    public IAsyncRelayCommand SaveSelectedCommand { get; }
    public IAsyncRelayCommand DeleteSelectedCommand { get; }
    public IRelayCommand NewPermanentCommand { get; }
    public IAsyncRelayCommand TogglePinCommand { get; }

    public async Task InitializeAsync() => await LoadAsync();

    partial void OnQuickContentChanged(string value) => NotifyQuickCommands();
    partial void OnQuickTitleChanged(string value) => NotifyQuickCommands();

    partial void OnEditContentChanged(string value) => SaveSelectedCommand.NotifyCanExecuteChanged();

    partial void OnSelectedPermanentChanged(Announcement? value)
    {
        if (value is null)
        {
            // Не очищаем редактор при «Новое» — StartNewPermanent сам ставит поля
        }
        else
        {
            EditTitle = value.Title;
            EditContent = value.Content;
            EditIsPinned = value.IsPinned;
        }

        ShowSelectedCommand.NotifyCanExecuteChanged();
        SaveSelectedCommand.NotifyCanExecuteChanged();
        DeleteSelectedCommand.NotifyCanExecuteChanged();
        TogglePinCommand.NotifyCanExecuteChanged();
    }

    private void NotifyQuickCommands()
    {
        ShowQuickCommand.NotifyCanExecuteChanged();
        SaveQuickCommand.NotifyCanExecuteChanged();
    }

    private bool CanShowQuick() => !string.IsNullOrWhiteSpace(QuickContent);

    private bool CanShowEditor() =>
        !string.IsNullOrWhiteSpace(EditContent) || SelectedPermanent is not null;

    private IAnnouncementService CreateService(IServiceScope scope) =>
        scope.ServiceProvider.GetRequiredService<IAnnouncementService>();

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
            var items = await CreateService(scope).GetPermanentAsync();
            var selectedId = SelectedPermanent?.Id;

            PermanentItems.Clear();
            foreach (var item in items)
            {
                PermanentItems.Add(item);
            }

            if (selectedId is Guid id)
            {
                SelectedPermanent = PermanentItems.FirstOrDefault(a => a.Id == id);
            }

            StatusMessage = PermanentItems.Count > 0
                ? $"Постоянных объявлений: {PermanentItems.Count}"
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

    private async Task ShowQuickAsync()
    {
        if (!CanShowQuick())
        {
            return;
        }

        await ProjectContentAsync(
            string.IsNullOrWhiteSpace(QuickTitle) ? "Объявление" : QuickTitle.Trim(),
            QuickContent);
        StatusMessage = "Быстрое объявление на экране";
    }

    private async Task SaveQuickAsync()
    {
        if (!CanShowQuick())
        {
            return;
        }

        try
        {
            using var scope = _services.CreateScope();
            var saved = await CreateService(scope).SaveAsync(new Announcement
            {
                Title = QuickTitle,
                Content = QuickContent,
                IsPermanent = true
            });

            await LoadAsync();
            SelectedPermanent = PermanentItems.FirstOrDefault(a => a.Id == saved.Id);
            StatusMessage = $"Сохранено: «{saved.Title}»";
        }
        catch (Exception ex)
        {
            await ErrorDialog.ShowAsync("Ошибка", ex);
        }
    }

    private void StartNewPermanent()
    {
        SelectedPermanent = null;
        EditTitle = string.Empty;
        EditContent = string.Empty;
        EditIsPinned = false;
        StatusMessage = "Новое объявление — заполните текст и нажмите «Сохранить»";
        ShowSelectedCommand.NotifyCanExecuteChanged();
        SaveSelectedCommand.NotifyCanExecuteChanged();
        DeleteSelectedCommand.NotifyCanExecuteChanged();
    }

    private async Task ShowSelectedAsync()
    {
        var content = !string.IsNullOrWhiteSpace(EditContent)
            ? EditContent
            : SelectedPermanent?.Content;

        if (string.IsNullOrWhiteSpace(content))
        {
            StatusMessage = "Нет текста для показа";
            return;
        }

        var title = !string.IsNullOrWhiteSpace(EditTitle)
            ? EditTitle.Trim()
            : SelectedPermanent?.Title ?? "Объявление";

        await ProjectContentAsync(title, content);
        StatusMessage = $"На экране: «{title}»";
    }

    private async Task SaveSelectedAsync()
    {
        if (string.IsNullOrWhiteSpace(EditContent))
        {
            StatusMessage = "Текст объявления не может быть пустым";
            return;
        }

        try
        {
            using var scope = _services.CreateScope();
            var entity = SelectedPermanent is null
                ? new Announcement { Content = EditContent }
                : new Announcement
                {
                    Id = SelectedPermanent.Id,
                    SortOrder = SelectedPermanent.SortOrder,
                    Content = EditContent
                };

            entity.Title = EditTitle;
            entity.Content = EditContent;
            entity.IsPinned = EditIsPinned;
            entity.IsPermanent = true;

            var saved = await CreateService(scope).SaveAsync(entity);
            await LoadAsync();
            SelectedPermanent = PermanentItems.FirstOrDefault(a => a.Id == saved.Id);
            StatusMessage = $"Сохранено: «{saved.Title}»";
        }
        catch (Exception ex)
        {
            await ErrorDialog.ShowAsync("Ошибка", ex);
        }
    }

    private async Task DeleteSelectedAsync()
    {
        if (SelectedPermanent is null)
        {
            return;
        }

        try
        {
            using var scope = _services.CreateScope();
            var id = SelectedPermanent.Id;
            var title = SelectedPermanent.Title;
            await CreateService(scope).DeleteAsync(id);
            SelectedPermanent = null;
            EditTitle = string.Empty;
            EditContent = string.Empty;
            EditIsPinned = false;
            await LoadAsync();
            StatusMessage = $"Удалено: «{title}»";
        }
        catch (Exception ex)
        {
            await ErrorDialog.ShowAsync("Ошибка", ex);
        }
    }

    private async Task TogglePinAsync()
    {
        if (SelectedPermanent is null)
        {
            return;
        }

        EditIsPinned = !EditIsPinned;
        await SaveSelectedAsync();
    }

    public async Task StartProjectionFromHotkeyAsync()
    {
        if (!string.IsNullOrWhiteSpace(EditContent) || SelectedPermanent is not null)
        {
            await ShowSelectedAsync();
            return;
        }

        if (CanShowQuick())
        {
            await ShowQuickAsync();
        }
    }

    private async Task ProjectContentAsync(string title, string content)
    {
        var song = BuildEphemeralSong(title, content);
        var live = _services.GetRequiredService<LiveControlViewModel>();
        await live.StartSongFromCatalogAsync(song);
    }

    internal static Song BuildEphemeralSong(string title, string content)
    {
        var songId = Guid.NewGuid();
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
