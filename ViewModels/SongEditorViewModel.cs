using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.IO;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ChyguiSlide.Data.Entities;
using ChyguiSlide.Data.Enums;
using ChyguiSlide.Services.Abstractions;
using ChyguiSlide.Services.Implementations;
using ChyguiSlide.Services.Models;
using Microsoft.EntityFrameworkCore;
using Windows.Storage;
using Windows.Storage.Pickers;
using WinRT.Interop;
using ChyguiSlide.Views.Dialogs;

namespace ChyguiSlide.ViewModels;

public sealed partial class SongEditorViewModel : ObservableRecipient
{
    private readonly ICatalogService _catalogService;
    private readonly IPresentationImportService _presentationImportService;
    private readonly IWebSongImportService _webSongImportService;
    private bool _isApplyingSong;
    private Guid? _currentSongId;
    public event EventHandler<Song>? SongSaved;
    private static readonly IReadOnlyDictionary<SectionType, string> SectionDisplayNames = new Dictionary<SectionType, string>
    {
        { SectionType.Verse, "Куплет" },
        { SectionType.Chorus, "Припев" }
    };
    private static string GetSectionDisplayName(SectionType sectionType) =>
        SectionDisplayNames.TryGetValue(sectionType, out var name)
            ? name
            : sectionType.ToString();

    public ObservableCollection<Song> Songs { get; } = new();
    public ObservableCollection<SectionEditorItem> Sections { get; } = new();
    public ObservableCollection<SongCollectionOption> CollectionOptions { get; } = new();

    [ObservableProperty]
    private Song? selectedSong;

    [ObservableProperty]
    private SectionEditorItem? selectedSection;

    [ObservableProperty]
    private SongCollectionOption? selectedCollectionOption;

    [ObservableProperty]
    private string? searchQuery;

    [ObservableProperty]
    private string title = string.Empty;

    [ObservableProperty]
    private string? numberText;

    [ObservableProperty]
    private string? subtitle;

    [ObservableProperty]
    private string? language;

    [ObservableProperty]
    private string? defaultKey;

    [ObservableProperty]
    private double? tempo;

    [ObservableProperty]
    private bool isFavorite;

    [ObservableProperty]
    private bool isPublished = true;

    [ObservableProperty]
    private bool isDirty;

    [ObservableProperty]
    private bool isBusy;

    [ObservableProperty]
    private bool isSaving;

    [ObservableProperty]
    private string? statusMessage;

    [ObservableProperty]
    private string? validationTitle;

    [ObservableProperty]
    private string? validationError;

    [ObservableProperty]
    private bool isValidationVisible;

    [ObservableProperty]
    private string previewText = string.Empty;

    public IReadOnlyList<SectionType> SectionTypeOptions { get; } = new[]
    {
        SectionType.Verse,
        SectionType.Chorus
    };

    public IAsyncRelayCommand InitializeCommand { get; }
    public IAsyncRelayCommand LoadSongsCommand { get; }
    public IAsyncRelayCommand SaveSongCommand { get; }
    public IAsyncRelayCommand ImportPresentationCommand { get; }
    public IRelayCommand CreateNewSongCommand { get; }
    public IRelayCommand AddSectionCommand { get; }
    public IRelayCommand RemoveSectionCommand { get; }
    public IRelayCommand MoveSectionUpCommand { get; }
    public IRelayCommand MoveSectionDownCommand { get; }
    public IRelayCommand RepeatChorusAfterVersesCommand { get; }
    public IRelayCommand RefreshPreviewCommand { get; }
    public IRelayCommand DismissValidationCommand { get; }
    public IRelayCommand SectionContentToSingleLineCommand { get; }

    public SongEditorViewModel(
        ICatalogService catalogService,
        IPresentationImportService presentationImportService,
        IWebSongImportService webSongImportService)
    {
        _catalogService = catalogService;
        _presentationImportService = presentationImportService;
        _webSongImportService = webSongImportService;

        InitializeCommand = new AsyncRelayCommand(token => InitializeAsync(token));
        LoadSongsCommand = new AsyncRelayCommand(token => LoadSongsAsync(token));
        SaveSongCommand = new AsyncRelayCommand(SaveSongAsync, CanSaveSong);
        ImportPresentationCommand = new AsyncRelayCommand(ImportPresentationAsync, CanImportPresentation);
        CreateNewSongCommand = new RelayCommand(CreateNewSong);
        AddSectionCommand = new RelayCommand(AddSection);
        RemoveSectionCommand = new RelayCommand(RemoveSection, () => SelectedSection is not null);
        MoveSectionUpCommand = new RelayCommand(() => MoveSection(-1), () => CanMoveSection(-1));
        MoveSectionDownCommand = new RelayCommand(() => MoveSection(1), () => CanMoveSection(1));
        RepeatChorusAfterVersesCommand = new RelayCommand(RepeatChorusAfterVerses, CanRepeatChorus);
        RefreshPreviewCommand = new RelayCommand(UpdatePreview);
        DismissValidationCommand = new RelayCommand(ClearValidation);
        SectionContentToSingleLineCommand = new RelayCommand(SectionContentToSingleLine, () => SelectedSection is not null);

        Sections.CollectionChanged += OnSectionsCollectionChanged;
    }

    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        await RefreshCollectionOptionsAsync(cancellationToken);

        if (Songs.Count > 0)
        {
            return;
        }

        await LoadSongsAsync(cancellationToken);
    }

    public async Task RefreshCollectionOptionsAsync(CancellationToken cancellationToken = default)
    {
        var collections = await _catalogService.GetSongCollectionsAsync(cancellationToken);
        var previous = SelectedCollectionOption;

        CollectionOptions.Clear();
        CollectionOptions.Add(SongCollectionOption.Unspecified);
        CollectionOptions.Add(SongCollectionOption.WithoutCollection);
        foreach (var collection in collections)
        {
            CollectionOptions.Add(new SongCollectionOption(collection.Name, collection));
        }

        if (previous is { Kind: SongCollectionChoiceKind.Specific, CollectionId: Guid id })
        {
            SelectedCollectionOption =
                CollectionOptions.FirstOrDefault(o => o.CollectionId == id)
                ?? SongCollectionOption.Unspecified;
        }
        else if (previous is { Kind: SongCollectionChoiceKind.WithoutCollection })
        {
            SelectedCollectionOption = SongCollectionOption.WithoutCollection;
        }
        else
        {
            SelectedCollectionOption = SongCollectionOption.Unspecified;
        }
    }

    public void ApplyDefaultCollection(Guid? collectionId)
    {
        // Для новой песни / импорта по умолчанию «Не выбрано» — пользователь должен выбрать явно
        SelectedCollectionOption = collectionId is Guid id
            ? CollectionOptions.FirstOrDefault(o => o.CollectionId == id) ?? SongCollectionOption.Unspecified
            : SongCollectionOption.Unspecified;
    }

    private async Task LoadSongsAsync(CancellationToken cancellationToken = default)
    {
        if (IsBusy)
        {
            return;
        }

        try
        {
            IsBusy = true;
            Songs.Clear();

            var results = string.IsNullOrWhiteSpace(SearchQuery)
                ? await _catalogService.GetSongsAsync(cancellationToken)
                : await _catalogService.SearchSongsAsync(SearchQuery, cancellationToken);

            foreach (var song in results.OrderBy(s => s.Title, StringComparer.CurrentCultureIgnoreCase))
            {
                Songs.Add(song);
            }

            StatusMessage = Songs.Count == 0
                ? "Песни не найдены. Создайте новую."
                : $"Загружено {Songs.Count} песен.";

            // Не трогаем SelectedSong: редактор открывается из каталога (новая / выбранная / импорт).
            // Автовыбор Songs.First() затирал CreateNewSong и подставлял чужую песню.
        }
        catch (Exception ex)
        {
            StatusMessage = null;
            await ErrorDialog.ShowAsync("Не удалось загрузить песни", ex);
        }
        finally
        {
            IsBusy = false;
            SaveSongCommand.NotifyCanExecuteChanged();
        }
    }

    private async Task SaveSongAsync()
    {
        if (!CanSaveSong())
        {
            ShowValidation("Нельзя сохранить", "Заполните название и все секции перед сохранением.");
            return;
        }

        if (SelectedCollectionOption is null || SelectedCollectionOption.IsUnspecified)
        {
            ShowValidation(
                "Сборник не выбран",
                "Выберите сборник в списке или пункт «Без сборника».");
            return;
        }

        ClearValidation();

        try
        {
            IsSaving = true;
            StatusMessage = "Сохраняем песню...";

            var songToSave = BuildSongModel();
            var savedSong = await _catalogService.UpsertSongAsync(songToSave);

            // Обновляем _currentSongId после сохранения
            _currentSongId = savedSong.Id;

            UpdateSongsCollection(savedSong);
            SelectedSong = Songs.FirstOrDefault(s => s.Id == savedSong.Id) ?? savedSong;

            StatusMessage = $"Песня «{savedSong.Title}» сохранена.";
            IsDirty = false;
            UpdatePreview();
            SongSaved?.Invoke(this, savedSong);
        }
        catch (Exception ex)
        {
            StatusMessage = null;
            ShowValidation("Не удалось сохранить песню", GetFriendlyError(ex));
        }
        finally
        {
            IsSaving = false;
            SaveSongCommand.NotifyCanExecuteChanged();
            ImportPresentationCommand.NotifyCanExecuteChanged();
        }
    }

    private void ShowValidation(string title, string message)
    {
        ValidationTitle = title;
        ValidationError = message;
        IsValidationVisible = true;
    }

    private void ClearValidation()
    {
        IsValidationVisible = false;
        ValidationTitle = null;
        ValidationError = null;
    }

    private bool CanImportPresentation()
    {
        return !IsBusy && !IsSaving;
    }

    private async Task ImportPresentationAsync()
    {
        if (!CanImportPresentation())
        {
            return;
        }

        StorageFile? file = null;

        try
        {
            IsBusy = true;
            ImportPresentationCommand.NotifyCanExecuteChanged();
            SaveSongCommand.NotifyCanExecuteChanged();

            file = await PickPresentationFileAsync();
            if (file is null)
            {
                StatusMessage = "Импорт отменён.";
                return;
            }

            var result = await _presentationImportService.ImportAsync(file.Path);
            ApplyImportedPresentation(result, file.Name);
        }
        catch (Exception ex)
        {
            StatusMessage = null;
            await ErrorDialog.ShowAsync("Не удалось импортировать", ex);
        }
        finally
        {
            IsBusy = false;
            ImportPresentationCommand.NotifyCanExecuteChanged();
            SaveSongCommand.NotifyCanExecuteChanged();
        }
    }

    private bool CanSaveSong()
    {
        return !IsBusy
               && !IsSaving
               && !string.IsNullOrWhiteSpace(Title)
               && Sections.Count > 0
               && Sections.All(s => !string.IsNullOrWhiteSpace(s.Content));
    }

    private void CreateNewSong()
    {
        _isApplyingSong = true;

        ClearValidation();
        _currentSongId = null;
        Title = string.Empty;
        NumberText = null;
        Subtitle = null;
        Language = null;
        DefaultKey = null;
        Tempo = null;
        IsFavorite = false;
        IsPublished = true;
        SelectedCollectionOption = SongCollectionOption.Unspecified;

        Sections.Clear();
        AddSection();

        // Сбрасываем выбор без LoadSongIntoEditor (OnSelectedSongChanged только при не-null)
        SelectedSong = null;
        IsDirty = false;
        StatusMessage = "Создайте новую песню и нажмите «Сохранить».";

        _isApplyingSong = false;
        UpdatePreview();
        SaveSongCommand.NotifyCanExecuteChanged();
        ImportPresentationCommand.NotifyCanExecuteChanged();
    }

    private void AddSection()
    {
        var section = new SectionEditorItem
        {
            Id = Guid.NewGuid(),
            SectionType = SectionType.Verse,
            Heading = "Куплет",
            Content = string.Empty,
            Notes = null
        };

        AttachSectionHandlers(section);
        Sections.Add(section);
        SelectedSection = section;
        RenumberSections();
        SetDirtyAndUpdatePreview();
    }

    private void RemoveSection()
    {
        if (SelectedSection is null)
        {
            return;
        }

        var index = Sections.IndexOf(SelectedSection);
        DetachSectionHandlers(SelectedSection);
        Sections.Remove(SelectedSection);

        if (Sections.Count == 0)
        {
            SelectedSection = null;
        }
        else
        {
            // Остаёмся на той же позиции (бывший следующий), либо на предыдущем, если удалили последний
            var newIndex = Math.Min(index, Sections.Count - 1);
            SelectedSection = Sections[newIndex];
        }

        RenumberSections();
        SetDirtyAndUpdatePreview();
    }

    private bool CanMoveSection(int direction)
    {
        if (SelectedSection is null)
        {
            return false;
        }

        var index = Sections.IndexOf(SelectedSection);
        var newIndex = index + direction;
        return newIndex >= 0 && newIndex < Sections.Count;
    }

    private void MoveSection(int direction)
    {
        if (!CanMoveSection(direction) || SelectedSection is null)
        {
            return;
        }

        var index = Sections.IndexOf(SelectedSection);
        Sections.Move(index, index + direction);
        RenumberSections();
        SelectedSection = Sections[index + direction];
        SetDirtyAndUpdatePreview();
    }

    private bool CanRepeatChorus()
    {
        return Sections.Any(s => s.SectionType == SectionType.Chorus)
               && Sections.Any(s => s.SectionType == SectionType.Verse);
    }

    private void RepeatChorusAfterVerses()
    {
        var chorus = Sections.FirstOrDefault(s => s.SectionType == SectionType.Chorus);
        if (chorus is null)
        {
            return;
        }

        SectionEditorItem CloneChorus()
        {
            return new SectionEditorItem
            {
                Id = Guid.NewGuid(),
                SectionType = SectionType.Chorus,
                Heading = chorus.Heading,
                Content = chorus.Content,
                Notes = chorus.Notes
            };
        }

        var index = 0;
        while (index < Sections.Count)
        {
            if (Sections[index].SectionType == SectionType.Verse)
            {
                var nextIndex = index + 1;
                var nextIsChorus = nextIndex < Sections.Count && Sections[nextIndex].SectionType == SectionType.Chorus;

                if (!nextIsChorus)
                {
                    var copy = CloneChorus();
                    AttachSectionHandlers(copy);
                    Sections.Insert(nextIndex, copy);
                }

                index = nextIndex + 1;
            }
            else
            {
                index++;
            }
        }

        RenumberSections();
        SetDirtyAndUpdatePreview();
        RepeatChorusAfterVersesCommand.NotifyCanExecuteChanged();
    }

    private void SectionContentToSingleLine()
    {
        if (SelectedSection is null)
        {
            return;
        }

        // Заменяем переносы строк на пробелы
        var content = SelectedSection.Content ?? string.Empty;
        var singleLine = System.Text.RegularExpressions.Regex.Replace(content, @"\r\n?|\n", " ");
        // Убираем лишние пробелы (если между строками уже были пробелы)
        singleLine = System.Text.RegularExpressions.Regex.Replace(singleLine, " +", " ").Trim();

        SelectedSection.Content = singleLine;
        SetDirtyAndUpdatePreview();
    }

    private void LoadSongIntoEditor(Song song)
    {
        _isApplyingSong = true;

        _currentSongId = song.Id;
        Title = song.Title;
        NumberText = song.Number?.ToString(CultureInfo.CurrentCulture);
        Subtitle = song.Subtitle;
        Language = song.Language;
        DefaultKey = song.DefaultKey;
        Tempo = song.Tempo;
        IsFavorite = song.IsFavorite;
        IsPublished = song.IsPublished;
        SelectedCollectionOption = song.CollectionId is Guid collectionId
            ? CollectionOptions.FirstOrDefault(o => o.CollectionId == collectionId)
              ?? SongCollectionOption.WithoutCollection
            : SongCollectionOption.WithoutCollection;

        Sections.Clear();

        foreach (var section in song.Sections.OrderBy(s => s.Order))
        {
            var editorItem = new SectionEditorItem
            {
                Id = section.Id,
                SectionType = section.SectionType,
                Heading = section.Heading,
                Content = section.Content,
                Notes = section.Notes
            };

            AttachSectionHandlers(editorItem);
            Sections.Add(editorItem);
        }

        SelectedSection = Sections.FirstOrDefault();
        IsDirty = false;
        StatusMessage = $"Редактируется песня «{song.Title}».";

        _isApplyingSong = false;
        UpdatePreview();
        SaveSongCommand.NotifyCanExecuteChanged();
    }

    private Song BuildSongModel()
    {
        var song = new Song
        {
            Id = _currentSongId ?? Guid.NewGuid(),
            Title = Title,
            Number = ParseSongNumber(NumberText),
            Subtitle = Subtitle,
            Language = Language,
            DefaultKey = DefaultKey,
            Tempo = Tempo.HasValue ? (int)Math.Round(Tempo.Value) : null,
            IsFavorite = IsFavorite,
            IsPublished = IsPublished,
            CollectionId = SelectedCollectionOption?.CollectionId,
            UpdatedAt = DateTime.UtcNow
        };

        if (_currentSongId is null)
        {
            song.CreatedAt = DateTime.UtcNow;
        }

        var order = 0;
        foreach (var section in Sections)
        {
            song.Sections.Add(new SongSection
            {
                Id = section.Id == Guid.Empty ? Guid.NewGuid() : section.Id,
                SongId = song.Id,
                SectionType = section.SectionType,
                Heading = section.Heading,
                Content = section.Content,
                Notes = section.Notes,
                Order = order++
            });
        }

        return song;
    }

    private void ApplyImportedPresentation(PresentationImportResult result, string fileName)
    {
        _isApplyingSong = true;

        _currentSongId = null;
        Title = string.IsNullOrWhiteSpace(result.Title)
            ? Path.GetFileNameWithoutExtension(fileName)
            : result.Title!;
        NumberText = null;
        Subtitle = null;
        Language = null;
        DefaultKey = null;
        Tempo = null;
        IsFavorite = false;
        IsPublished = true;
        SelectedCollectionOption = SongCollectionOption.Unspecified;

        Sections.Clear();
        var slides = result.Slides ?? Array.Empty<PresentationSlide>();
        var order = 0;
        foreach (var slide in slides)
        {
            var verseNumber = order + 1;
            // Весь текст слайда — в Content; заголовок всегда «Куплет N»
            var text = string.IsNullOrWhiteSpace(slide.Content)
                ? slide.Heading
                : slide.Content;

            var section = new SectionEditorItem
            {
                Id = Guid.NewGuid(),
                SectionType = SectionType.Verse,
                Heading = $"Куплет {verseNumber}",
                Content = string.IsNullOrWhiteSpace(text) ? "(пусто)" : text.Trim(),
                Notes = null,
                Order = order++
            };
            AttachSectionHandlers(section);
            Sections.Add(section);
        }

        SelectedSection = Sections.FirstOrDefault();
        StatusMessage = slides.Count == 0
            ? "Текст в презентации не найден."
            : $"Импортировано {slides.Count} слайдов из «{fileName}».";
        IsDirty = slides.Count > 0;

        _isApplyingSong = false;
        UpdatePreview();
        SaveSongCommand.NotifyCanExecuteChanged();
    }

    public async Task ImportFromUrlAsync(string url)
    {
        IsBusy = true;
        StatusMessage = "Загрузка песни с сайта…";
        try
        {
            var result = await _webSongImportService.ImportFromUrlAsync(url);
            ApplyImportedWebSong(result);
        }
        catch
        {
            StatusMessage = null;
            throw;
        }
        finally
        {
            IsBusy = false;
            SaveSongCommand.NotifyCanExecuteChanged();
            ImportPresentationCommand.NotifyCanExecuteChanged();
        }
    }

    public void ApplyImportedWebSong(WebSongImportResult result)
    {
        _isApplyingSong = true;

        _currentSongId = null;
        Title = string.IsNullOrWhiteSpace(result.Title) ? "Импортированная песня" : result.Title!;
        NumberText = null;
        Subtitle = null;
        Language = null;
        DefaultKey = null;
        Tempo = null;
        IsFavorite = false;
        IsPublished = true;
        SelectedCollectionOption = SongCollectionOption.Unspecified;

        Sections.Clear();

        var verses = new List<(string Heading, string Content)>();
        (string Heading, string Content)? chorus = null;

        foreach (var (heading, content, isChorus) in result.Sections)
        {
            if (isChorus)
            {
                chorus ??= (string.IsNullOrWhiteSpace(heading) ? "Припев" : heading, content);
            }
            else
            {
                verses.Add((string.IsNullOrWhiteSpace(heading) ? $"Куплет {verses.Count + 1}" : heading, content));
            }
        }

        // Куплет → припев → куплет → припев …
        var order = 0;
        void AddSectionItem(string heading, string content, SectionType type)
        {
            var section = new SectionEditorItem
            {
                Id = Guid.NewGuid(),
                SectionType = type,
                Heading = heading,
                Content = content,
                Notes = null,
                Order = order++
            };
            AttachSectionHandlers(section);
            Sections.Add(section);
        }

        if (verses.Count > 0)
        {
            foreach (var verse in verses)
            {
                AddSectionItem(verse.Heading, verse.Content, SectionType.Verse);
                if (chorus is { } c)
                {
                    AddSectionItem(c.Heading, c.Content, SectionType.Chorus);
                }
            }
        }
        else if (chorus is { } onlyChorus)
        {
            AddSectionItem(onlyChorus.Heading, onlyChorus.Content, SectionType.Chorus);
        }
        else
        {
            foreach (var (heading, content, isChorus) in result.Sections)
            {
                AddSectionItem(heading, content, isChorus ? SectionType.Chorus : SectionType.Verse);
            }
        }

        if (Sections.Count == 0)
        {
            AddSection();
        }

        SelectedSection = Sections.FirstOrDefault();
        IsDirty = Sections.Count > 0 && Sections.Any(s => !string.IsNullOrWhiteSpace(s.Content));
        StatusMessage = chorus is not null && verses.Count > 0
            ? $"Импортировано: {verses.Count} купл. + припев после каждого."
            : string.IsNullOrWhiteSpace(result.Warning)
                ? $"Импортировано {Sections.Count} частей с сайта."
                : result.Warning;

        _isApplyingSong = false;
        UpdatePreview();
        SaveSongCommand.NotifyCanExecuteChanged();
        RepeatChorusAfterVersesCommand.NotifyCanExecuteChanged();
    }

    private async Task<StorageFile?> PickPresentationFileAsync()
    {
        var picker = new FileOpenPicker
        {
            SuggestedStartLocation = PickerLocationId.DocumentsLibrary,
            ViewMode = PickerViewMode.List
        };

        picker.FileTypeFilter.Add(".pptx");
        picker.FileTypeFilter.Add(".ppt");
        picker.FileTypeFilter.Add(".odp");

        InitializeWithWindow.Initialize(picker, App.MainWindowHandle);
        return await picker.PickSingleFileAsync();
    }

    private static int? ParseSongNumber(string? input)
    {
        if (string.IsNullOrWhiteSpace(input))
        {
            return null;
        }

        return int.TryParse(input, NumberStyles.Integer, CultureInfo.CurrentCulture, out var number) && number > 0
            ? number
            : null;
    }

    private void UpdateSongsCollection(Song savedSong)
    {
        var existing = Songs.FirstOrDefault(s => s.Id == savedSong.Id);
        if (existing is not null)
        {
            var index = Songs.IndexOf(existing);
            Songs[index] = savedSong;
        }
        else
        {
            Songs.Add(savedSong);
        }

        SortSongsInPlace();
    }

    private void SortSongsInPlace()
    {
        if (Songs.Count <= 1)
        {
            return;
        }

        var ordered = Songs.OrderBy(s => s.Title, StringComparer.CurrentCultureIgnoreCase).ToList();
        Songs.Clear();
        foreach (var song in ordered)
        {
            Songs.Add(song);
        }
    }

    private void OnSectionsCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (e.OldItems is not null)
        {
            foreach (SectionEditorItem item in e.OldItems)
            {
                DetachSectionHandlers(item);
            }
        }

        if (e.NewItems is not null)
        {
            foreach (SectionEditorItem item in e.NewItems)
            {
                AttachSectionHandlers(item);
            }
        }

        if (!_isApplyingSong)
        {
            SetDirtyAndUpdatePreview();
        }

        MoveSectionUpCommand.NotifyCanExecuteChanged();
        MoveSectionDownCommand.NotifyCanExecuteChanged();
        RemoveSectionCommand.NotifyCanExecuteChanged();
        RepeatChorusAfterVersesCommand.NotifyCanExecuteChanged();
        SaveSongCommand.NotifyCanExecuteChanged();
    }

    private void AttachSectionHandlers(SectionEditorItem section)
    {
        section.PropertyChanged += OnSectionPropertyChanged;
    }

    private void DetachSectionHandlers(SectionEditorItem section)
    {
        section.PropertyChanged -= OnSectionPropertyChanged;
    }

    private void OnSectionPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (_isApplyingSong)
        {
            return;
        }

        SetDirtyAndUpdatePreview();
        if (string.Equals(e.PropertyName, nameof(SectionEditorItem.SectionType), StringComparison.Ordinal))
        {
            RepeatChorusAfterVersesCommand.NotifyCanExecuteChanged();
        }

        SaveSongCommand.NotifyCanExecuteChanged();
    }

    private void RenumberSections()
    {
        for (var i = 0; i < Sections.Count; i++)
        {
            Sections[i].Order = i;
        }
    }

    private void UpdatePreview()
    {
        var builder = new StringBuilder();
        var parsedNumber = ParseSongNumber(NumberText);

        if (!string.IsNullOrWhiteSpace(Title))
        {
            var headingTitle = Title.ToUpper(CultureInfo.CurrentCulture);
            if (parsedNumber.HasValue)
            {
                headingTitle = $"№{parsedNumber.Value:000}  {headingTitle}";
            }

            builder.AppendLine(headingTitle);
            builder.AppendLine(new string('=', Math.Max(headingTitle.Length, 3)));
        }

        builder.AppendLine();

        var tempoText = Tempo.HasValue
            ? $"{Tempo.Value.ToString("0", CultureInfo.CurrentCulture)} BPM"
            : "—";

        builder.Append("Язык: ").Append(string.IsNullOrWhiteSpace(Language) ? "—" : Language);
        builder.Append("    Тональность: ").Append(string.IsNullOrWhiteSpace(DefaultKey) ? "—" : DefaultKey);
        builder.Append("    Темп: ").Append(tempoText);
        builder.Append("    Избранное: ").Append(IsFavorite ? "да" : "нет");
        builder.Append("    Статус: ").Append(IsPublished ? "опубликована" : "черновик");
        builder.AppendLine();
        builder.AppendLine(new string('-', 64));
        builder.AppendLine();

        var orderedSections = Sections.OrderBy(s => s.Order).ToList();
        if (orderedSections.Count == 0)
        {
            builder.AppendLine("Добавьте секцию, чтобы увидеть предварительный просмотр.");
        }
        else
        {
            for (var i = 0; i < orderedSections.Count; i++)
            {
                var section = orderedSections[i];
                var marker = ReferenceEquals(section, SelectedSection) ? "▶" : " ";
                var number = (i + 1).ToString("00", CultureInfo.CurrentCulture);
                var displayName = GetSectionDisplayName(section.SectionType);
                var heading = string.IsNullOrWhiteSpace(section.Heading)
                    ? displayName
                    : section.Heading.Trim();

                builder.Append(marker)
                    .Append(' ')
                    .Append(number)
                    .Append(" • ")
                    .Append('[').Append(displayName).Append(']')
                    .Append(' ')
                    .AppendLine(heading);

                if (!string.IsNullOrWhiteSpace(section.Content))
                {
                    builder.AppendLine(section.Content.Trim());
                }
                else
                {
                    builder.AppendLine("(пусто)");
                }

                if (!string.IsNullOrWhiteSpace(section.Notes))
                {
                    builder.AppendLine($"   ⓘ Заметки: {section.Notes.Trim()}");
                }

                if (i < orderedSections.Count - 1)
                {
                    builder.AppendLine();
                    builder.AppendLine(new string('-', 48));
                    builder.AppendLine();
                }
            }
        }

        PreviewText = builder.ToString().TrimEnd();
    }

    private void SetDirtyAndUpdatePreview()
    {
        if (_isApplyingSong)
        {
            return;
        }

        IsDirty = true;
        UpdatePreview();
    }

    private static string GetFriendlyError(Exception ex)
    {
        if (ex is DbUpdateException dbEx && dbEx.InnerException is not null)
        {
            var message = dbEx.InnerException.Message;

            if (message.Contains("SectionTiming", StringComparison.OrdinalIgnoreCase))
            {
                return "Не удалось сохранить тайминг секции. Попробуйте ещё раз или временно очистите тайминг у секций.";
            }

            if (message.Contains("UNIQUE", StringComparison.OrdinalIgnoreCase) &&
                message.Contains("Number", StringComparison.OrdinalIgnoreCase))
            {
                return "Такой номер песни уже существует. Укажите другой номер или оставьте поле пустым.";
            }

            if (message.Contains("row", StringComparison.OrdinalIgnoreCase) &&
                message.Contains("affected 0", StringComparison.OrdinalIgnoreCase))
            {
                return "Не удалось обновить запись. Попробуйте ещё раз. Если не поможет — перезагрузите список песен, затем сохраните.";
            }
        }

        return ex.Message;
    }

    partial void OnSelectedSongChanged(Song? value)
    {
        if (value is not null)
        {
            LoadSongIntoEditor(value);
        }
    }

    partial void OnTitleChanged(string value)
    {
        MarkDirtyFromProperty();
    }

    partial void OnNumberTextChanged(string? value) => MarkDirtyFromProperty();
    partial void OnSubtitleChanged(string? value) => MarkDirtyFromProperty();
    partial void OnLanguageChanged(string? value) => MarkDirtyFromProperty();
    partial void OnDefaultKeyChanged(string? value) => MarkDirtyFromProperty();
    partial void OnTempoChanged(double? value) => MarkDirtyFromProperty();
    partial void OnIsFavoriteChanged(bool value) => MarkDirtyFromProperty();
    partial void OnIsPublishedChanged(bool value) => MarkDirtyFromProperty();
    partial void OnSelectedCollectionOptionChanged(SongCollectionOption? value)
    {
        if (value is not null && !value.IsUnspecified)
        {
            ClearValidation();
        }

        MarkDirtyFromProperty();
    }

    partial void OnSelectedSectionChanged(SectionEditorItem? value)
    {
        foreach (var section in Sections)
        {
            section.IsSelected = ReferenceEquals(section, value);
        }

        RemoveSectionCommand.NotifyCanExecuteChanged();
        MoveSectionUpCommand.NotifyCanExecuteChanged();
        MoveSectionDownCommand.NotifyCanExecuteChanged();
        if (_isApplyingSong)
        {
            return;
        }

        UpdatePreview();
    }

    private void MarkDirtyFromProperty()
    {
        if (_isApplyingSong)
        {
            return;
        }

        IsDirty = true;
        UpdatePreview();
        SaveSongCommand.NotifyCanExecuteChanged();
    }

    public sealed partial class SectionEditorItem : ObservableObject
    {
        [ObservableProperty]
        private Guid id;

        [ObservableProperty]
        private SectionType sectionType;

        [ObservableProperty]
        private string? heading;

        [ObservableProperty]
        private string content = string.Empty;

        [ObservableProperty]
        private string? notes;

        [ObservableProperty]
        private int order;

        [ObservableProperty]
        private bool isSelected;

        /// <summary>Заголовок для общего шаблона SelectableCard.</summary>
        public string Title =>
            string.IsNullOrWhiteSpace(Heading) ? "Без названия" : Heading.Trim();

        partial void OnHeadingChanged(string? value) => OnPropertyChanged(nameof(Title));
    }
}

