using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.Globalization;
using System.Linq;
using ChyguiSlide.Services;
using ChyguiSlide.Services.Models;
using ChyguiSlide.ViewModels;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI;
using Microsoft.UI.Text;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Navigation;
using Windows.UI;
using System.Threading.Tasks;

namespace ChyguiSlide.Views;

public sealed partial class BiblePage : Page
{
    private const int BookColumns = 11;
    private const int ChapterColumns = 8;
    private const int VerseColumns = 6;

    private static readonly Brush CellStroke =
        new SolidColorBrush(Color.FromArgb(0x40, 0x00, 0x00, 0x00));

    private static readonly Brush ChapterFill =
        new SolidColorBrush(Color.FromArgb(0xFF, 0x8B, 0x6B, 0x3D));

    private static readonly Brush VerseFill =
        new SolidColorBrush(Color.FromArgb(0xFF, 0xC4, 0xA5, 0x74));

    private static readonly Brush WhiteText = new SolidColorBrush(Colors.White);
    private static readonly Brush VerseText =
        new SolidColorBrush(Color.FromArgb(0xFF, 0x1A, 0x12, 0x08));
    private static readonly Brush SecondaryWhite =
        new SolidColorBrush(Color.FromArgb(0xE6, 0xFF, 0xFF, 0xFF));

    private static readonly Dictionary<string, Brush> BookColorCache = new(StringComparer.OrdinalIgnoreCase);

    private bool _booksDirty;
    private bool _chaptersDirty;
    private bool _versesDirty;
    private bool _rebuildQueued;
    private Brush? _accentBrush;

    public BibleViewModel ViewModel { get; }

    public BiblePage()
    {
        InitializeComponent();
        ViewModel = App.AppHost.Services.GetRequiredService<BibleViewModel>();
        DataContext = ViewModel;
        ViewModel.PropertyChanged += OnViewModelPropertyChanged;
        ViewModel.SearchFocusRequested += OnSearchFocusRequested;
        ViewModel.Books.CollectionChanged += OnBooksChanged;
        ViewModel.Chapters.CollectionChanged += OnChaptersChanged;
        ViewModel.Verses.CollectionChanged += OnVersesChanged;
    }

    private async void OnPageLoaded(object sender, RoutedEventArgs e)
    {
        await ViewModel.InitializeAsync();
        SyncChapterSelection();
        MarkAllTablesDirty();
        QueueTableRebuild();
        if (string.IsNullOrWhiteSpace(GetActiveSearchBox().Text) && ViewModel.IsSearchMode)
        {
            ViewModel.ApplySearch(null);
        }

        await FocusSearchBoxIfRequestedAsync();
    }

    protected override void OnNavigatedFrom(NavigationEventArgs e)
    {
        base.OnNavigatedFrom(e);
        // Без отписки старые экземпляры страницы «съедают» RequestSearchFocus
        // (ConsumePendingSearchFocusRequest) — горячая клавиша перестаёт фокусировать поиск.
        ViewModel.SearchFocusRequested -= OnSearchFocusRequested;
        SearchBox.Text = string.Empty;
        GridSearchBox.Text = string.Empty;
        ViewModel.ApplySearch(null);
    }

    protected override void OnNavigatedTo(NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);
        ViewModel.SearchFocusRequested -= OnSearchFocusRequested;
        ViewModel.SearchFocusRequested += OnSearchFocusRequested;
    }

    private void OnViewModelPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(BibleViewModel.SelectedChapter) or nameof(BibleViewModel.Chapters))
        {
            SyncChapterSelection();
        }

        if (e.PropertyName is nameof(BibleViewModel.PickerLayout))
        {
            MarkAllTablesDirty();
            QueueTableRebuild();
        }

        if (e.PropertyName is nameof(BibleViewModel.SelectedBook)
            or nameof(BibleViewModel.SelectedChapter)
            or nameof(BibleViewModel.SelectedVerse))
        {
            DispatcherQueue.TryEnqueue(UpdateTableSelectionChrome);
        }
    }

    private void OnBooksChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        _booksDirty = true;
        QueueTableRebuild();
    }

    private void OnChaptersChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        _chaptersDirty = true;
        QueueTableRebuild();
    }

    private void OnVersesChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        _versesDirty = true;
        QueueTableRebuild();
    }

    private void MarkAllTablesDirty()
    {
        _booksDirty = true;
        _chaptersDirty = true;
        _versesDirty = true;
    }

    private void QueueTableRebuild()
    {
        if (_rebuildQueued || !ViewModel.IsGridPickerLayout)
        {
            return;
        }

        _rebuildQueued = true;
        DispatcherQueue.TryEnqueue(() =>
        {
            _rebuildQueued = false;
            if (!ViewModel.IsGridPickerLayout)
            {
                return;
            }

            _accentBrush ??= ThemeBrushHelper.Get("AccentFillColorDefaultBrush", this)
                             ?? new SolidColorBrush(Color.FromArgb(0xFF, 0x51, 0x2B, 0xD4));

            if (_booksDirty)
            {
                RebuildBookTable();
                _booksDirty = false;
            }

            if (_chaptersDirty)
            {
                RebuildChapterTable();
                _chaptersDirty = false;
            }

            if (_versesDirty)
            {
                RebuildVerseTable();
                _versesDirty = false;
            }
        });
    }

    private void RebuildBookTable()
    {
        BuildUniformTable(
            BookTable,
            ViewModel.Books.Count,
            BookColumns,
            index =>
            {
                var book = ViewModel.Books[index];
                var selected = ReferenceEquals(book, ViewModel.SelectedBook);
                var cell = CreateBookCell(book, selected);
                cell.Tag = book;
                cell.Tapped += OnBookCellTapped;
                return cell;
            });
    }

    private void RebuildChapterTable()
    {
        BuildUniformTable(
            ChapterTable,
            ViewModel.Chapters.Count,
            ChapterColumns,
            index =>
            {
                var chapter = ViewModel.Chapters[index];
                var selected = ViewModel.SelectedChapter == chapter;
                var cell = CreateNumberCell(
                    ChapterFill,
                    WhiteText,
                    chapter.ToString(CultureInfo.InvariantCulture),
                    selected);
                cell.Tag = chapter;
                cell.Tapped += OnChapterCellTapped;
                return cell;
            });
    }

    private void RebuildVerseTable()
    {
        BuildUniformTable(
            VerseTable,
            ViewModel.Verses.Count,
            VerseColumns,
            index =>
            {
                var verse = ViewModel.Verses[index];
                var selected = ReferenceEquals(verse, ViewModel.SelectedVerse);
                var cell = CreateNumberCell(
                    VerseFill,
                    VerseText,
                    verse.VerseNumber.ToString(CultureInfo.InvariantCulture),
                    selected);
                cell.Tag = verse;
                cell.Tapped += OnVerseCellTapped;
                return cell;
            });
    }

    private static void BuildUniformTable(
        Grid table,
        int itemCount,
        int columns,
        Func<int, FrameworkElement> createCell)
    {
        table.Children.Clear();
        table.RowDefinitions.Clear();
        table.ColumnDefinitions.Clear();

        if (itemCount <= 0)
        {
            return;
        }

        columns = Math.Clamp(columns, 1, itemCount);
        var rows = (int)Math.Ceiling(itemCount / (double)columns);

        for (var c = 0; c < columns; c++)
        {
            table.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        }

        for (var r = 0; r < rows; r++)
        {
            table.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        }

        for (var i = 0; i < itemCount; i++)
        {
            var cell = createCell(i);
            Grid.SetRow(cell, i / columns);
            Grid.SetColumn(cell, i % columns);
            table.Children.Add(cell);
        }
    }

    private Border CreateBookCell(BibleBook book, bool selected)
    {
        var panel = new StackPanel
        {
            Spacing = 1,
            Children =
            {
                new TextBlock
                {
                    Text = book.Abbreviation,
                    FontSize = 28,
                    FontWeight = FontWeights.Bold,
                    Foreground = WhiteText,
                    HorizontalAlignment = HorizontalAlignment.Center
                },
                new TextBlock
                {
                    Text = book.RussianName,
                    FontSize = 12,
                    Foreground = SecondaryWhite,
                    HorizontalAlignment = HorizontalAlignment.Center,
                    TextTrimming = TextTrimming.CharacterEllipsis,
                    MaxLines = 1
                }
            }
        };

        return new Border
        {
            Background = GetBookBrush(book.CategoryColorHex),
            BorderBrush = selected ? _accentBrush : CellStroke,
            BorderThickness = new Thickness(2),
            Child = new Viewbox
            {
                Stretch = Stretch.Uniform,
                Margin = new Thickness(3),
                Child = panel
            },
            HorizontalAlignment = HorizontalAlignment.Stretch,
            VerticalAlignment = VerticalAlignment.Stretch
        };
    }

    private Border CreateNumberCell(Brush background, Brush foreground, string text, bool selected) =>
        new()
        {
            Background = background,
            BorderBrush = selected ? _accentBrush : CellStroke,
            BorderThickness = new Thickness(2),
            Child = new Viewbox
            {
                Stretch = Stretch.Uniform,
                Margin = new Thickness(4),
                Child = new TextBlock
                {
                    Text = text,
                    FontSize = 32,
                    FontWeight = FontWeights.Bold,
                    Foreground = foreground,
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center
                }
            },
            HorizontalAlignment = HorizontalAlignment.Stretch,
            VerticalAlignment = VerticalAlignment.Stretch
        };

    private void UpdateTableSelectionChrome()
    {
        if (!ViewModel.IsGridPickerLayout)
        {
            return;
        }

        _accentBrush ??= ThemeBrushHelper.Get("AccentFillColorDefaultBrush", this)
                         ?? new SolidColorBrush(Color.FromArgb(0xFF, 0x51, 0x2B, 0xD4));

        foreach (var child in BookTable.Children.OfType<Border>())
        {
            var selected = child.Tag is BibleBook book && ReferenceEquals(book, ViewModel.SelectedBook);
            child.BorderBrush = selected ? _accentBrush : CellStroke;
        }

        foreach (var child in ChapterTable.Children.OfType<Border>())
        {
            var selected = child.Tag is int chapter && ViewModel.SelectedChapter == chapter;
            child.BorderBrush = selected ? _accentBrush : CellStroke;
        }

        foreach (var child in VerseTable.Children.OfType<Border>())
        {
            var selected = child.Tag is BibleVerseItem verse && ReferenceEquals(verse, ViewModel.SelectedVerse);
            child.BorderBrush = selected ? _accentBrush : CellStroke;
        }
    }

    private void OnBookCellTapped(object sender, TappedRoutedEventArgs e)
    {
        if (sender is Border { Tag: BibleBook book })
        {
            ViewModel.SelectedBook = book;
        }
    }

    private void OnChapterCellTapped(object sender, TappedRoutedEventArgs e)
    {
        if (sender is Border { Tag: int chapter })
        {
            ViewModel.SelectedChapter = chapter;
        }
    }

    private void OnVerseCellTapped(object sender, TappedRoutedEventArgs e)
    {
        if (sender is Border { Tag: BibleVerseItem verse })
        {
            ViewModel.SelectedVerse = verse;
        }
    }

    private void SyncChapterSelection()
    {
        if (ViewModel.SelectedChapter is int chapter && ChapterList.Items.Contains(chapter))
        {
            ChapterList.SelectedItem = chapter;
        }
        else if (ViewModel.SelectedChapter is null)
        {
            ChapterList.SelectedItem = null;
        }
    }

    private void OnChapterSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (ChapterList.SelectedItem is int chapter)
        {
            ViewModel.SelectedChapter = chapter;
        }
    }

    private void OnSearchSubmitted(AutoSuggestBox sender, AutoSuggestBoxQuerySubmittedEventArgs args)
    {
        ViewModel.ApplySearch(args.QueryText);
        if (!ViewModel.IsSearchMode)
        {
            sender.Text = string.Empty;
            SyncSearchBoxes(sender);
            SyncChapterSelection();
            MarkAllTablesDirty();
            QueueTableRebuild();
        }
    }

    private void OnSearchTextChanged(AutoSuggestBox sender, AutoSuggestBoxTextChangedEventArgs args)
    {
        if (args.Reason != AutoSuggestionBoxTextChangeReason.UserInput)
        {
            return;
        }

        SyncSearchBoxes(sender);
        ViewModel.ApplySearch(sender.Text);

        if (!ViewModel.IsSearchMode)
        {
            SyncChapterSelection();
            MarkAllTablesDirty();
            QueueTableRebuild();
        }
    }

    private void SyncSearchBoxes(AutoSuggestBox source)
    {
        var other = ReferenceEquals(source, SearchBox) ? GridSearchBox : SearchBox;
        if (other.Text != source.Text)
        {
            other.Text = source.Text;
        }
    }

    private AutoSuggestBox GetActiveSearchBox() =>
        ViewModel.IsGridPickerLayout ? GridSearchBox : SearchBox;

    private void OnSearchResultSelected(object sender, SelectionChangedEventArgs e)
    {
        if (sender is ListView { SelectedItem: BibleVerseItem item })
        {
            ViewModel.NavigateToSearchResult(item);
            SyncChapterSelection();
            MarkAllTablesDirty();
            QueueTableRebuild();
        }
    }

    private bool _isHandlingSearchFocus;

    private async Task FocusSearchBoxIfRequestedAsync()
    {
        if (!ViewModel.ConsumePendingSearchFocusRequest())
        {
            return;
        }

        // Ждём layout (список/сетка) — иначе Focus на Collapsed AutoSuggestBox молча падает.
        await Task.Delay(50);
        var box = GetActiveSearchBox();
        box.Focus(FocusState.Keyboard);
        try
        {
            box.IsSuggestionListOpen = false;
        }
        catch
        {
            // ignore
        }
    }

    private async void OnSearchFocusRequested()
    {
        if (_isHandlingSearchFocus)
        {
            return;
        }

        _isHandlingSearchFocus = true;
        try
        {
            await FocusSearchBoxIfRequestedAsync();
        }
        finally
        {
            _isHandlingSearchFocus = false;
        }
    }

    private static Brush GetBookBrush(string hex)
    {
        if (BookColorCache.TryGetValue(hex, out var cached))
        {
            return cached;
        }

        var cleaned = (hex ?? string.Empty).Trim();
        if (cleaned.StartsWith('#'))
        {
            cleaned = cleaned[1..];
        }

        Brush brush = ChapterFill;
        if (cleaned.Length == 6
            && uint.TryParse(cleaned, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var rgb))
        {
            brush = new SolidColorBrush(Color.FromArgb(
                0xFF,
                (byte)((rgb >> 16) & 0xFF),
                (byte)((rgb >> 8) & 0xFF),
                (byte)(rgb & 0xFF)));
        }

        BookColorCache[hex] = brush;
        return brush;
    }
}
