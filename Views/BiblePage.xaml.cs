using System;
using System.Collections.Specialized;
using ChyguiSlide.Services;
using ChyguiSlide.Services.Models;
using ChyguiSlide.ViewModels;
using ChyguiSlide.Views.UiAnimation;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Navigation;
using System.Threading.Tasks;

namespace ChyguiSlide.Views;

public sealed partial class BiblePage : Page
{
    private ListSelectionStripeBinder? _verseStripe;
    private CatalogLookaheadPreview? _lookaheadPreview;

    public static readonly DependencyProperty ModernBookTileFontSizeProperty =
        DependencyProperty.Register(
            nameof(ModernBookTileFontSize),
            typeof(double),
            typeof(BiblePage),
            new PropertyMetadata(14.0));

    public double ModernBookTileFontSize
    {
        get => (double)GetValue(ModernBookTileFontSizeProperty);
        set => SetValue(ModernBookTileFontSizeProperty, value);
    }

    public BibleViewModel ViewModel { get; }

    public BiblePage()
    {
        InitializeComponent();
        ViewModel = App.AppHost.Services.GetRequiredService<BibleViewModel>();
        DataContext = ViewModel;
        ViewModel.PropertyChanged += OnViewModelPropertyChanged;
        ViewModel.SearchFocusRequested += OnSearchFocusRequested;
        ViewModel.OldTestamentBooks.CollectionChanged += OnModernBookListsChanged;
        ViewModel.NewTestamentBooks.CollectionChanged += OnModernBookListsChanged;
    }

    private async void OnPageLoaded(object sender, RoutedEventArgs e)
    {
        await ApplyModernLayoutAsync();
        await ViewModel.InitializeAsync();
        SyncChapterSelection();
        if (string.IsNullOrWhiteSpace(GetActiveSearchBox().Text) && ViewModel.IsSearchMode)
        {
            ViewModel.ApplySearch(null);
        }

        await FocusSearchBoxIfRequestedAsync();
        _ = RefreshLookaheadPreviewAsync();
    }

    protected override void OnNavigatedFrom(NavigationEventArgs e)
    {
        base.OnNavigatedFrom(e);
        // Без отписки старые экземпляры страницы «съедают» RequestSearchFocus
        // (ConsumePendingSearchFocusRequest) — горячая клавиша перестаёт фокусировать поиск.
        ViewModel.SearchFocusRequested -= OnSearchFocusRequested;
        SearchBoxModern.Text = string.Empty;
        ViewModel.ApplySearch(null);
    }

    protected override void OnNavigatedTo(NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);
        ViewModel.SearchFocusRequested -= OnSearchFocusRequested;
        ViewModel.SearchFocusRequested += OnSearchFocusRequested;
        _ = ApplyModernLayoutAsync();
    }

    private void OnViewModelPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(BibleViewModel.SelectedChapter) or nameof(BibleViewModel.Chapters))
        {
            SyncChapterSelection();
        }

        if (e.PropertyName is nameof(BibleViewModel.SelectedBook))
        {
            SyncModernBookSelection();
        }

        if (e.PropertyName is nameof(BibleViewModel.SelectedBook)
            or nameof(BibleViewModel.SelectedChapter)
            or nameof(BibleViewModel.IsSearchMode))
        {
            _verseStripe?.ResetReady();
            _ = RefreshLookaheadPreviewAsync();
        }
        else if (e.PropertyName is nameof(BibleViewModel.SelectedVerse))
        {
            _verseStripe?.RequestUpdate(animate: true);
            _ = RefreshLookaheadPreviewAsync();
        }
    }

    private void SyncChapterSelection()
    {
        if (ChapterGridModern is null)
        {
            return;
        }

        if (ViewModel.SelectedChapter is int chapter && ChapterGridModern.Items.Contains(chapter))
        {
            ChapterGridModern.SelectedItem = chapter;
        }
        else if (ViewModel.SelectedChapter is null)
        {
            ChapterGridModern.SelectedItem = null;
        }
    }

    private void OnChapterSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (sender is ListViewBase { SelectedItem: int chapter })
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
            SyncChapterSelection();
        }
    }

    private void OnSearchTextChanged(AutoSuggestBox sender, AutoSuggestBoxTextChangedEventArgs args)
    {
        if (args.Reason != AutoSuggestionBoxTextChangeReason.UserInput)
        {
            return;
        }

        ViewModel.ApplySearch(sender.Text);

        if (!ViewModel.IsSearchMode)
        {
            SyncChapterSelection();
        }
    }

    private AutoSuggestBox GetActiveSearchBox() => SearchBoxModern;

    private void OnSearchResultSelected(object sender, SelectionChangedEventArgs e)
    {
        if (sender is ListView { SelectedItem: BibleVerseItem item })
        {
            ViewModel.NavigateToSearchResult(item);
            SyncChapterSelection();
        }
    }

    private bool _isHandlingSearchFocus;

    private async Task FocusSearchBoxIfRequestedAsync()
    {
        if (!ViewModel.ConsumePendingSearchFocusRequest())
        {
            return;
        }

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

    private async Task ApplyModernLayoutAsync()
    {
        await ApplyPreviewCanvasAsync();
        EnsureModernVerseStripe();
        EnsureLookaheadPreview();
        SyncChapterSelection();
        SyncModernBookSelection();
        ApplyModernBookTileLayout();
        _ = RefreshLookaheadPreviewAsync();
    }

    private void EnsureModernVerseStripe()
    {
        if (VerseListModern is null || ModernVerseStripe is null || ModernVerseHost is null)
        {
            return;
        }

        _verseStripe ??= new ListSelectionStripeBinder(
            VerseListModern,
            ModernVerseStripe,
            ModernVerseHost,
            () => ViewModel.SelectedVerse,
            DispatcherQueue);
        _verseStripe.Attach();
        _verseStripe.RequestUpdate(animate: false);
    }

    private void EnsureLookaheadPreview()
    {
        if (BibleWebPreview is null || BiblePreviewIdleHint is null)
        {
            return;
        }

        _lookaheadPreview ??= new CatalogLookaheadPreview(BibleWebPreview, BiblePreviewIdleHint);
    }

    private async Task ApplyPreviewCanvasAsync()
    {
        var (width, height) = await ProjectionOutputSize.GetAsync();
        if (BiblePreviewCanvas is not null)
        {
            ProjectionOutputSize.ApplyCanvas(BiblePreviewCanvas, width, height);
        }

        BibleWebPreview?.ApplyOutputSize(width, height);
    }

    private async Task RefreshLookaheadPreviewAsync()
    {
        if (_lookaheadPreview is null)
        {
            return;
        }

        var verse = ViewModel.SelectedVerse;
        var content = verse is null
            ? null
            : $"{verse.VerseNumber}  {verse.Text}";
        await _lookaheadPreview.ShowContentAsync(verse?.Reference, content, verse?.Reference);
    }

    private bool _syncingModernBookSelection;

    private const int ModernBookColumns = 3;
    private const double ModernBookTileMinHeight = 40;
    private const double ModernBookGridViewChromeEach = 8;
    private const double ModernBookLayoutFudge = 4;
    private const double ModernBookTileFontMin = 10;
    private const double ModernBookTileFontMax = 18;
    private const double ModernBookTileTextPaddingVertical = 6;

    private void OnModernBookListsChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        DispatcherQueue.TryEnqueue(DispatcherQueuePriority.Low, ApplyModernBookTileLayout);
    }

    private bool _applyingModernBookTiles;

    private void OnModernBookGridLoaded(object sender, RoutedEventArgs e)
    {
        ApplyModernBookTileLayout();
    }

    private void OnModernBooksHostSizeChanged(object sender, SizeChangedEventArgs e)
    {
        ApplyModernBookTileLayout();
    }

    private void ApplyModernBookTileLayout()
    {
        if (ModernBooksScroll is null || ModernBooksHost is null)
        {
            return;
        }

        if (_applyingModernBookTiles)
        {
            return;
        }

        _applyingModernBookTiles = true;
        try
        {
            var viewportWidth = ModernBooksScroll.ActualWidth;
            var viewportHeight = ModernBooksScroll.ActualHeight;
            if (viewportWidth < 90 || viewportHeight < 80)
            {
                return;
            }

            var innerWidth = Math.Max(
                90,
                viewportWidth - ModernBooksHost.Margin.Left - ModernBooksHost.Margin.Right);
            if (double.IsNaN(ModernBooksHost.Width) || Math.Abs(ModernBooksHost.Width - innerWidth) > 0.5)
            {
                ModernBooksHost.Width = innerWidth;
            }

            var tileWidth = Math.Floor(innerWidth / ModernBookColumns);
            var (tileHeight, enableScroll) = ComputeModernBookTileHeight(viewportHeight);
            UpdateModernBookTileFontSize(tileHeight);

            if (enableScroll)
            {
                ModernBooksScroll.VerticalScrollMode = ScrollMode.Enabled;
                ModernBooksScroll.VerticalScrollBarVisibility = ScrollBarVisibility.Hidden;
            }
            else
            {
                ModernBooksScroll.VerticalScrollMode = ScrollMode.Disabled;
                ModernBooksScroll.VerticalScrollBarVisibility = ScrollBarVisibility.Disabled;
            }

            ApplyModernBookTileSize(OldTestamentListModern, tileWidth, tileHeight);
            ApplyModernBookTileSize(NewTestamentListModern, tileWidth, tileHeight);
        }
        finally
        {
            _applyingModernBookTiles = false;
        }
    }

    private (double Height, bool EnableScroll) ComputeModernBookTileHeight(double viewportHeight)
    {
        var rows = CountTileRows(ViewModel.OldTestamentBooks.Count)
                   + CountTileRows(ViewModel.NewTestamentBooks.Count);
        if (rows < 1)
        {
            return (ModernBookTileMinHeight, false);
        }

        var otHeader = OldTestamentHeaderModern;
        var ntHeader = NewTestamentHeaderModern;
        var otHeight = otHeader?.ActualHeight ?? 0;
        var ntHeight = ntHeader?.ActualHeight ?? 0;
        if (otHeight < 4 || ntHeight < 4)
        {
            DispatcherQueue.TryEnqueue(DispatcherQueuePriority.Low, ApplyModernBookTileLayout);
            otHeight = Math.Max(otHeight, 16);
            ntHeight = Math.Max(ntHeight, 16);
        }

        // TextBlock.ActualHeight excludes Margin; StackPanel adds header margins.
        var chrome = otHeight + VerticalMargin(otHeader)
                     + ntHeight + VerticalMargin(ntHeader)
                     + VerticalMargin(ModernBooksHost)
                     + ModernBookGridViewChromeEach * 2;
        var available = viewportHeight - chrome - ModernBookLayoutFudge;
        var fitted = Math.Floor(available / rows);
        if (fitted < ModernBookTileMinHeight)
        {
            return (ModernBookTileMinHeight, true);
        }

        return (fitted, false);
    }

    private void UpdateModernBookTileFontSize(double tileHeight)
    {
        var itemMarginVertical = 2;
        var inner = tileHeight - itemMarginVertical - ModernBookTileTextPaddingVertical;
        var size = Math.Clamp(Math.Floor(inner), ModernBookTileFontMin, ModernBookTileFontMax);
        if (Math.Abs(ModernBookTileFontSize - size) > 0.1)
        {
            ModernBookTileFontSize = size;
        }
    }

    private static double VerticalMargin(FrameworkElement? element)
        => element is null ? 0 : element.Margin.Top + element.Margin.Bottom;

    private static int CountTileRows(int itemCount)
        => itemCount <= 0 ? 0 : (int)Math.Ceiling(itemCount / (double)ModernBookColumns);

    private void ApplyModernBookTileSize(GridView? grid, double tileWidth, double tileHeight)
    {
        if (grid is null)
        {
            return;
        }

        grid.Width = tileWidth * ModernBookColumns;
        grid.ClearValue(HeightProperty);
        var wrap = grid.ItemsPanelRoot as ItemsWrapGrid ?? FindDescendant<ItemsWrapGrid>(grid);
        if (wrap is null)
        {
            DispatcherQueue.TryEnqueue(DispatcherQueuePriority.Low, ApplyModernBookTileLayout);
            return;
        }

        wrap.Orientation = Orientation.Horizontal;
        wrap.MaximumRowsOrColumns = ModernBookColumns;
        wrap.ItemWidth = Math.Max(24, tileWidth);
        wrap.ItemHeight = Math.Max(ModernBookTileMinHeight, tileHeight);
    }

    private void OnModernBookSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_syncingModernBookSelection)
        {
            return;
        }

        if (sender is not ListViewBase { SelectedItem: BibleBook book })
        {
            return;
        }

        if (!ReferenceEquals(ViewModel.SelectedBook, book))
        {
            ViewModel.SelectedBook = book;
        }

        SyncModernBookSelection();
    }

    private void SyncModernBookSelection()
    {
        if (OldTestamentListModern is null || NewTestamentListModern is null)
        {
            return;
        }

        _syncingModernBookSelection = true;
        try
        {
            var selected = ViewModel.SelectedBook;
            OldTestamentListModern.SelectedItem = selected is not null && !selected.IsNewTestament
                ? selected
                : null;
            NewTestamentListModern.SelectedItem = selected is not null && selected.IsNewTestament
                ? selected
                : null;
        }
        finally
        {
            _syncingModernBookSelection = false;
        }
    }

    private static T? FindDescendant<T>(DependencyObject root) where T : DependencyObject
    {
        var count = VisualTreeHelper.GetChildrenCount(root);
        for (var i = 0; i < count; i++)
        {
            var child = VisualTreeHelper.GetChild(root, i);
            if (child is T match)
            {
                return match;
            }

            var nested = FindDescendant<T>(child);
            if (nested is not null)
            {
                return nested;
            }
        }

        return null;
    }
}
