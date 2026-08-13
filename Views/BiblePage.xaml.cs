using System;
using System.Collections.Specialized;
using ChyguiSlide.ViewModels;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;
using System.Threading.Tasks;

namespace ChyguiSlide.Views;

public sealed partial class BiblePage : Page
{
    private const double TileGap = 4;
    private const double BookTileMinSize = 52;
    private const double NumberTileMinSize = 32;

    public BibleViewModel ViewModel { get; }

    public BiblePage()
    {
        InitializeComponent();
        ViewModel = App.AppHost.Services.GetRequiredService<BibleViewModel>();
        DataContext = ViewModel;
        ViewModel.PropertyChanged += OnViewModelPropertyChanged;
        ViewModel.SearchFocusRequested += OnSearchFocusRequested;
        ViewModel.Books.CollectionChanged += OnPickerItemsChanged;
        ViewModel.Chapters.CollectionChanged += OnPickerItemsChanged;
        ViewModel.Verses.CollectionChanged += OnPickerItemsChanged;
        BookGrid.Loaded += OnPickerGridLoaded;
        ChapterGrid.Loaded += OnPickerGridLoaded;
        VerseNumberGrid.Loaded += OnPickerGridLoaded;
    }

    private async void OnPageLoaded(object sender, RoutedEventArgs e)
    {
        await ViewModel.InitializeAsync();
        SyncChapterSelection();
        UpdatePickerTileSizes();
        if (string.IsNullOrWhiteSpace(GetActiveSearchBox().Text) && ViewModel.IsSearchMode)
        {
            ViewModel.ApplySearch(null);
        }

        await FocusSearchBoxIfRequestedAsync();
    }

    protected override void OnNavigatedFrom(NavigationEventArgs e)
    {
        base.OnNavigatedFrom(e);
        SearchBox.Text = string.Empty;
        GridSearchBox.Text = string.Empty;
        ViewModel.ApplySearch(null);
    }

    private void OnViewModelPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(BibleViewModel.SelectedChapter) or nameof(BibleViewModel.Chapters))
        {
            SyncChapterSelection();
        }

        if (e.PropertyName is nameof(BibleViewModel.PickerLayout))
        {
            DispatcherQueue.TryEnqueue(UpdatePickerTileSizes);
        }
    }

    private void OnPickerItemsChanged(object? sender, NotifyCollectionChangedEventArgs e) =>
        DispatcherQueue.TryEnqueue(UpdatePickerTileSizes);

    private void OnPickerGridLoaded(object sender, RoutedEventArgs e) => UpdatePickerTileSizes();

    private void OnPickerGridSizeChanged(object sender, SizeChangedEventArgs e) => UpdatePickerTileSizes();

    private bool _isSizingPickerTiles;

    private void UpdatePickerTileSizes()
    {
        if (_isSizingPickerTiles || !ViewModel.IsGridPickerLayout)
        {
            return;
        }

        _isSizingPickerTiles = true;
        try
        {
            SizeWrapGrid(BookGrid, BookGridHost, ViewModel.Books.Count, 11, BookTileMinSize, headerHeight: 0);
            SizeWrapGrid(ChapterGrid, ChapterGridHost, ViewModel.Chapters.Count, 10, NumberTileMinSize, headerHeight: 28);
            SizeWrapGrid(VerseNumberGrid, VerseGridHost, ViewModel.Verses.Count, 8, NumberTileMinSize, headerHeight: 28);
        }
        finally
        {
            _isSizingPickerTiles = false;
        }
    }

    private static void SizeWrapGrid(
        GridView grid,
        FrameworkElement host,
        int itemCount,
        int preferredColumns,
        double minItemSize,
        double headerHeight)
    {
        if (grid.ItemsPanelRoot is not ItemsWrapGrid wrap || host.ActualWidth <= 0 || host.ActualHeight <= 0)
        {
            return;
        }

        var padding = host is Border border ? border.Padding : new Thickness();
        var width = Math.Max(1, host.ActualWidth - padding.Left - padding.Right);
        var height = Math.Max(1, host.ActualHeight - padding.Top - padding.Bottom - headerHeight);

        var count = Math.Max(1, itemCount);
        var maxColumns = Math.Clamp(Math.Min(preferredColumns, count), 1, count);

        var bestColumns = 1;
        var bestSize = minItemSize;

        for (var columns = 1; columns <= maxColumns; columns++)
        {
            var rows = (int)Math.Ceiling(count / (double)columns);
            var sizeByWidth = (width / columns) - TileGap;
            var sizeByHeight = (height / rows) - TileGap;
            var size = Math.Min(sizeByWidth, sizeByHeight);
            if (size < minItemSize)
            {
                continue;
            }

            // Берём самую крупную квадратную плитку; при равенстве — ближе к preferredColumns.
            if (size > bestSize + 0.5 || (Math.Abs(size - bestSize) <= 0.5 && columns > bestColumns))
            {
                bestSize = size;
                bestColumns = columns;
            }
        }

        wrap.Orientation = Orientation.Horizontal;
        wrap.MaximumRowsOrColumns = bestColumns;
        wrap.ItemWidth = bestSize;
        wrap.ItemHeight = bestSize;
    }

    private void SyncChapterSelection()
    {
        SyncChapterListSelection(ChapterList);
        SyncChapterListSelection(ChapterGrid);
    }

    private void SyncChapterListSelection(ListViewBase list)
    {
        if (ViewModel.SelectedChapter is int chapter && list.Items.Contains(chapter))
        {
            list.SelectedItem = chapter;
        }
        else if (ViewModel.SelectedChapter is null)
        {
            list.SelectedItem = null;
        }
    }

    private void OnChapterSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (ChapterList.SelectedItem is int chapter)
        {
            ViewModel.SelectedChapter = chapter;
        }
    }

    private void OnChapterGridSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (ChapterGrid.SelectedItem is int chapter)
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
        }
    }

    private async Task FocusSearchBoxIfRequestedAsync()
    {
        if (!ViewModel.ConsumePendingSearchFocusRequest())
        {
            return;
        }

        await Task.Yield();
        GetActiveSearchBox().Focus(FocusState.Programmatic);
    }

    private async void OnSearchFocusRequested()
    {
        await FocusSearchBoxIfRequestedAsync();
    }
}
