using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ChyguiSlide.Data.Entities;
using ChyguiSlide.Data.Enums;
using ChyguiSlide.Services.Abstractions;
using ChyguiSlide.Services.Bible;
using ChyguiSlide.Services.Models;
using ChyguiSlide.Views.Dialogs;
using Microsoft.Extensions.DependencyInjection;

namespace ChyguiSlide.ViewModels;

public partial class BibleViewModel : ObservableObject
{
    private readonly IBibleService _bibleService;
    private readonly IServiceProvider _services;
    private readonly List<BibleBook> _allBooks = new();
    private bool _suppressAutoChapterSelect;
    private bool _pendingSearchFocusRequest;
    public event Action? SearchFocusRequested;

    public BibleViewModel(
        IBibleService bibleService,
        IServiceProvider services)
    {
        _bibleService = bibleService;
        _services = services;

        Books = new ObservableCollection<BibleBook>();
        OldTestamentBooks = new ObservableCollection<BibleBook>();
        NewTestamentBooks = new ObservableCollection<BibleBook>();
        Chapters = new ObservableCollection<int>();
        Verses = new ObservableCollection<BibleVerseItem>();
        SearchResults = new ObservableCollection<BibleVerseItem>();

        TestamentFilterItems = new[]
        {
            new BibleTestamentFilterItem("Вся", BibleTestamentFilter.All),
            new BibleTestamentFilterItem("Ветхий Завет", BibleTestamentFilter.OldTestament),
            new BibleTestamentFilterItem("Новый Завет", BibleTestamentFilter.NewTestament)
        };
        selectedTestamentFilter = TestamentFilterItems[0];

        StartProjectionCommand = new AsyncRelayCommand(StartProjectionAsync, CanStartProjection);
        ProjectFromVerseCommand = new AsyncRelayCommand<BibleVerseItem?>(ProjectFromVerseAsync, v => v is not null);
    }

    public ObservableCollection<BibleBook> Books { get; }
    public ObservableCollection<BibleBook> OldTestamentBooks { get; }
    public ObservableCollection<BibleBook> NewTestamentBooks { get; }
    public ObservableCollection<int> Chapters { get; }
    public ObservableCollection<BibleVerseItem> Verses { get; }
    public ObservableCollection<BibleVerseItem> SearchResults { get; }

    public IReadOnlyList<BibleTestamentFilterItem> TestamentFilterItems { get; }

    public string TranslationName => _bibleService.TranslationName;

    [ObservableProperty]
    private bool isBusy;

    [ObservableProperty]
    private string? statusMessage;

    [ObservableProperty]
    private string? searchTerm;

    [ObservableProperty]
    private bool isSearchMode;

    [ObservableProperty]
    private BibleTestamentFilterItem? selectedTestamentFilter;

    [ObservableProperty]
    private BibleBook? selectedBook;

    [ObservableProperty]
    private int? selectedChapter;

    [ObservableProperty]
    private BibleVerseItem? selectedVerse;

    public IAsyncRelayCommand StartProjectionCommand { get; }
    public IAsyncRelayCommand<BibleVerseItem?> ProjectFromVerseCommand { get; }

    public string SelectionSummary
    {
        get
        {
            if (SelectedBook is null)
            {
                return "Выберите книгу";
            }

            if (SelectedChapter is null)
            {
                return SelectedBook.RussianName;
            }

            if (SelectedVerse is null)
            {
                return $"{SelectedBook.RussianName} {SelectedChapter}";
            }

            return $"{SelectedBook.RussianName} {SelectedChapter}:{SelectedVerse.VerseNumber}";
        }
    }

    public string SelectedBookTitle => SelectedBook?.RussianName ?? "Выберите книгу";

    public string SelectedChapterTitle =>
        SelectedChapter is int chapter ? $"Глава {chapter}" : string.Empty;

    public async Task InitializeAsync()
    {
        if (Books.Count > 0)
        {
            return;
        }

        try
        {
            IsBusy = true;
            StatusMessage = "Загрузка Библии…";
            await _bibleService.EnsureLoadedAsync();

            _allBooks.Clear();
            _allBooks.AddRange(_bibleService.GetBooks());
            RefreshBooks(preserveSelection: false);

            StatusMessage = $"{TranslationName} · {Books.Count} книг";
        }
        catch (Exception ex)
        {
            StatusMessage = null;
            await ErrorDialog.ShowAsync("Не удалось загрузить Библию", ex);
        }
        finally
        {
            IsBusy = false;
        }
    }

    partial void OnSelectedTestamentFilterChanged(BibleTestamentFilterItem? value)
    {
        if (_allBooks.Count == 0)
        {
            return;
        }

        RefreshBooks(preserveSelection: true);

        if (!string.IsNullOrWhiteSpace(SearchTerm))
        {
            ApplySearch(SearchTerm);
        }
        else
        {
            StatusMessage = $"{TranslationName} · {Books.Count} книг";
        }
    }

    private void RefreshBooks(bool preserveSelection)
    {
        var previousId = preserveSelection ? SelectedBook?.BookId : null;
        var filter = SelectedTestamentFilter?.Filter ?? BibleTestamentFilter.All;

        Books.Clear();
        foreach (var book in _allBooks.Where(b => MatchesTestamentFilter(b, filter)))
        {
            Books.Add(book);
        }

        RebuildBookGroups();

        if (previousId is not null)
        {
            var stillVisible = Books.FirstOrDefault(b =>
                string.Equals(b.BookId, previousId, StringComparison.OrdinalIgnoreCase));
            if (stillVisible is not null)
            {
                SelectedBook = stillVisible;
                return;
            }
        }

        SelectedBook = Books.FirstOrDefault();
    }

    private static bool MatchesTestamentFilter(BibleBook book, BibleTestamentFilter filter) =>
        filter switch
        {
            BibleTestamentFilter.OldTestament => !book.IsNewTestament,
            BibleTestamentFilter.NewTestament => book.IsNewTestament,
            _ => true
        };

    private void RebuildBookGroups()
    {
        OldTestamentBooks.Clear();
        NewTestamentBooks.Clear();
        foreach (var book in _allBooks.Where(b => !b.IsNewTestament).OrderBy(b => b.Order))
        {
            OldTestamentBooks.Add(book);
        }

        foreach (var book in _allBooks.Where(b => b.IsNewTestament).OrderBy(b => b.Order))
        {
            NewTestamentBooks.Add(book);
        }
    }

    partial void OnSelectedBookChanged(BibleBook? value)
    {
        Chapters.Clear();
        Verses.Clear();
        SelectedChapter = null;
        SelectedVerse = null;
        OnPropertyChanged(nameof(SelectionSummary));
        OnPropertyChanged(nameof(SelectedBookTitle));
        OnPropertyChanged(nameof(SelectedChapterTitle));
        StartProjectionCommand.NotifyCanExecuteChanged();

        if (value is null)
        {
            return;
        }

        var chapters = _bibleService.GetChapters(value.BookId);
        for (var i = 0; i < chapters.Count; i++)
        {
            Chapters.Add(chapters[i]);
        }

        if (!_suppressAutoChapterSelect)
        {
            SelectedChapter = Chapters.Count > 0 ? Chapters[0] : null;
        }
    }

    partial void OnSelectedChapterChanged(int? value)
    {
        Verses.Clear();
        SelectedVerse = null;
        OnPropertyChanged(nameof(SelectionSummary));
        OnPropertyChanged(nameof(SelectedChapterTitle));
        StartProjectionCommand.NotifyCanExecuteChanged();

        if (SelectedBook is null || value is null or 0)
        {
            return;
        }

        var verses = _bibleService.GetVerses(SelectedBook.BookId, value.Value);
        for (var i = 0; i < verses.Count; i++)
        {
            Verses.Add(new BibleVerseItem(verses[i], SelectedBook));
        }

        SelectedVerse = Verses.FirstOrDefault();
    }

    partial void OnSelectedVerseChanged(BibleVerseItem? value)
    {
        foreach (var verse in Verses)
        {
            verse.IsSelected = ReferenceEquals(verse, value);
        }

        OnPropertyChanged(nameof(SelectionSummary));
        StartProjectionCommand.NotifyCanExecuteChanged();
        ProjectFromVerseCommand.NotifyCanExecuteChanged();
    }

    public void ApplySearch(string? query)
    {
        SearchTerm = query;
        SearchResults.Clear();

        if (string.IsNullOrWhiteSpace(query))
        {
            IsSearchMode = false;
            return;
        }

        var term = query.Trim();

        // Быстрый переход: «2пар 15 8», «Екк 11:10», «Ин 3 16»
        if (BibleReferenceParser.TryParse(term, out var reference)
            && TryNavigateToReference(reference))
        {
            return;
        }

        IsSearchMode = true;

        // Сначала книги по названию/сокращению (в т.ч. частичные: «екк», «пар»)
        var matchingBooks = Books
            .Where(b => BookMatchesSearchToken(b, term))
            .ToList();

        if (matchingBooks.Count == 1 && term.Length >= 2)
        {
            // Только книга без главы — перейти к ней
            if (!term.Any(char.IsDigit)
                || BibleBookCatalog.TryResolveBook(term, out var onlyBookId)
                   && string.Equals(onlyBookId, matchingBooks[0].BookId, StringComparison.OrdinalIgnoreCase))
            {
                IsSearchMode = false;
                SelectedBook = matchingBooks[0];
                return;
            }
        }

        foreach (var verse in _bibleService.Search(term))
        {
            var book = _allBooks.FirstOrDefault(b =>
                string.Equals(b.BookId, verse.BookId, StringComparison.OrdinalIgnoreCase));
            if (book is null)
            {
                continue;
            }

            var filter = SelectedTestamentFilter?.Filter ?? BibleTestamentFilter.All;
            if (!MatchesTestamentFilter(book, filter))
            {
                continue;
            }

            SearchResults.Add(new BibleVerseItem(verse, book));
        }

        StatusMessage = SearchResults.Count > 0
            ? $"Найдено: {SearchResults.Count}"
            : matchingBooks.Count > 0
                ? $"Книги: {matchingBooks.Count}. Уточните запрос или выберите книгу слева."
                : "Ничего не найдено";
    }

    private static bool BookMatchesSearchToken(BibleBook book, string term)
    {
        if (book.RussianName.Contains(term, StringComparison.CurrentCultureIgnoreCase)
            || book.Abbreviation.Contains(term, StringComparison.CurrentCultureIgnoreCase)
            || book.EnglishName.Contains(term, StringComparison.CurrentCultureIgnoreCase)
            || book.BookId.Contains(term, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        return BibleBookCatalog.TryResolveBook(term, out var id)
               && string.Equals(id, book.BookId, StringComparison.OrdinalIgnoreCase);
    }

    private bool TryNavigateToReference(BibleReferenceQuery reference)
    {
        var book = Books.FirstOrDefault(b =>
            string.Equals(b.BookId, reference.BookId, StringComparison.OrdinalIgnoreCase))
            ?? _allBooks.FirstOrDefault(b =>
                string.Equals(b.BookId, reference.BookId, StringComparison.OrdinalIgnoreCase));
        if (book is null)
        {
            return false;
        }

        if (!Books.Contains(book))
        {
            SelectedTestamentFilter = TestamentFilterItems[0];
            book = Books.FirstOrDefault(b =>
                string.Equals(b.BookId, reference.BookId, StringComparison.OrdinalIgnoreCase)) ?? book;
        }

        var chapters = _bibleService.GetChapters(book.BookId);
        if (!chapters.Contains(reference.Chapter))
        {
            return false;
        }

        IsSearchMode = false;
        SearchTerm = null;
        SearchResults.Clear();

        _suppressAutoChapterSelect = true;
        SelectedBook = book;
        _suppressAutoChapterSelect = false;
        SelectedChapter = reference.Chapter;

        if (reference.Verse is int verseNumber)
        {
            SelectedVerse = Verses.FirstOrDefault(v => v.VerseNumber == verseNumber)
                            ?? Verses.FirstOrDefault();
        }
        else
        {
            SelectedVerse = Verses.FirstOrDefault();
        }

        StatusMessage = SelectedVerse is not null
            ? $"{book.RussianName} {reference.Chapter}:{SelectedVerse.VerseNumber}"
            : $"{book.RussianName} {reference.Chapter}";
        return true;
    }

    public void NavigateToSearchResult(BibleVerseItem? item)
    {
        if (item is null)
        {
            return;
        }

        IsSearchMode = false;
        SearchTerm = null;
        SearchResults.Clear();

        var book = Books.FirstOrDefault(b =>
            string.Equals(b.BookId, item.BookId, StringComparison.OrdinalIgnoreCase))
            ?? _allBooks.FirstOrDefault(b =>
                string.Equals(b.BookId, item.BookId, StringComparison.OrdinalIgnoreCase));
        if (book is null)
        {
            return;
        }

        if (!Books.Contains(book))
        {
            SelectedTestamentFilter = TestamentFilterItems[0];
            book = Books.FirstOrDefault(b =>
                string.Equals(b.BookId, item.BookId, StringComparison.OrdinalIgnoreCase)) ?? book;
        }

        _suppressAutoChapterSelect = true;
        SelectedBook = book;
        _suppressAutoChapterSelect = false;

        SelectedChapter = item.Chapter;
        SelectedVerse = Verses.FirstOrDefault(v => v.VerseNumber == item.VerseNumber);
    }

    private bool CanStartProjection() =>
        SelectedBook is not null && SelectedChapter is not null && SelectedVerse is not null;

    private async Task StartProjectionAsync()
    {
        await ProjectFromVerseAsync(SelectedVerse);
    }

    private async Task ProjectFromVerseAsync(BibleVerseItem? start)
    {
        if (SelectedBook is null || SelectedChapter is null || start is null)
        {
            return;
        }

        // Вся глава в трансляции, старт с выбранного стиха
        var chapterVerses = _bibleService.GetPassage(
            SelectedBook.BookId,
            SelectedChapter.Value,
            fromVerse: 1);

        if (chapterVerses.Count == 0)
        {
            StatusMessage = "Нет стихов для показа.";
            return;
        }

        var startIndex = 0;
        for (var i = 0; i < chapterVerses.Count; i++)
        {
            if (chapterVerses[i].Verse == start.VerseNumber)
            {
                startIndex = i;
                break;
            }
        }

        var last = chapterVerses[^1].Verse;
        var title = startIndex == 0 && chapterVerses.Count > 1
            ? $"{SelectedBook.RussianName} {SelectedChapter}:1–{last}"
            : $"{SelectedBook.RussianName} {SelectedChapter}:{start.VerseNumber}–{last}";

        if (chapterVerses.Count == 1)
        {
            title = $"{SelectedBook.RussianName} {SelectedChapter}:{start.VerseNumber}";
        }

        var song = BuildEphemeralSong(title, SelectedBook, chapterVerses);
        var live = _services.GetRequiredService<LiveControlViewModel>();
        await live.StartSongFromCatalogAsync(song, startIndex);
        StatusMessage = null;
    }

    /// <summary>
    /// Собрать эфемерную «песню» для главы (с метаданными для перехода ←/→).
    /// </summary>
    public Song? BuildChapterProjectionSong(string bookId, int chapter, int fromVerse = 1)
    {
        var book = _bibleService.GetBooks().FirstOrDefault(b =>
            string.Equals(b.BookId, bookId, StringComparison.OrdinalIgnoreCase));
        if (book is null)
        {
            return null;
        }

        var passage = _bibleService.GetPassage(bookId, chapter, fromVerse);
        if (passage.Count == 0)
        {
            return null;
        }

        var title = $"{book.RussianName} {chapter}:{passage[0].Verse}";
        if (passage.Count > 1)
        {
            title = $"{book.RussianName} {chapter}:{passage[0].Verse}–{passage[^1].Verse}";
        }

        return BuildEphemeralSong(title, book, passage);
    }

    /// <summary>
    /// Синхронизация выбора на странице Библии с текущим показом.
    /// </summary>
    public void SyncSelection(string bookId, int chapter, int? verse = null)
    {
        if (Books.Count == 0)
        {
            return;
        }

        var book = Books.FirstOrDefault(b =>
            string.Equals(b.BookId, bookId, StringComparison.OrdinalIgnoreCase));
        if (book is null)
        {
            return;
        }

        _suppressAutoChapterSelect = true;
        SelectedBook = book;
        _suppressAutoChapterSelect = false;
        SelectedChapter = chapter;
        SelectedVerse = verse is int v
            ? Verses.FirstOrDefault(x => x.VerseNumber == v) ?? Verses.FirstOrDefault()
            : Verses.FirstOrDefault();
    }

    /// <summary>
    /// Запуск с Библии по F5.
    /// </summary>
    public async Task StartProjectionFromHotkeyAsync()
    {
        if (!CanStartProjection())
        {
            if (SelectedBook is not null && SelectedChapter is not null && Verses.Count > 0)
            {
                SelectedVerse ??= Verses[0];
            }
            else
            {
                return;
            }
        }

        await StartProjectionAsync();
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

    private static Song BuildEphemeralSong(string title, BibleBook book, IReadOnlyList<BibleVerse> passage)
    {
        var songId = Guid.NewGuid();
        var chapter = passage[0].Chapter;
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

        return new Song
        {
            Id = songId,
            Title = title,
            Subtitle = $"{book.RussianName} · Синодальный",
            Language = "ru",
            // Метаданные для перехода между главами на Трансляции (не каталог)
            DefaultKey = $"bible:{book.BookId}:{chapter}",
            Sections = sections
        };
    }
}

public sealed partial class BibleVerseItem : ObservableObject
{
    public BibleVerseItem(BibleVerse verse, BibleBook book)
    {
        Verse = verse;
        Book = book;
    }

    public BibleVerse Verse { get; }
    public BibleBook Book { get; }

    public string BookId => Verse.BookId;
    public int Chapter => Verse.Chapter;
    public int VerseNumber => Verse.Verse;
    public string Text => Verse.Text;
    /// <summary>Заголовок карточки (общий шаблон SelectableCard).</summary>
    public string Title => Reference;
    /// <summary>Текст карточки (общий шаблон SelectableCard).</summary>
    public string Content => Verse.Text;
    public string Reference => $"{Book.RussianName} {Verse.Chapter}:{Verse.Verse}";
    public string Preview => $"{Verse.Verse}. {Verse.Text}";

    [ObservableProperty]
    private bool isSelected;
}

public sealed record BibleTestamentFilterItem(string Title, BibleTestamentFilter Filter);
