using ChyguiSlide.ViewModels;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;

namespace ChyguiSlide.Views;

public sealed partial class BiblePage : Page
{
    public BibleViewModel ViewModel { get; }

    public BiblePage()
    {
        InitializeComponent();
        ViewModel = App.AppHost.Services.GetRequiredService<BibleViewModel>();
        DataContext = ViewModel;
        ViewModel.PropertyChanged += OnViewModelPropertyChanged;
    }

    private async void OnPageLoaded(object sender, RoutedEventArgs e)
    {
        await ViewModel.InitializeAsync();
        SyncChapterSelection();
        if (string.IsNullOrWhiteSpace(SearchBox.Text) && ViewModel.IsSearchMode)
        {
            ViewModel.ApplySearch(null);
        }
    }

    protected override void OnNavigatedFrom(NavigationEventArgs e)
    {
        base.OnNavigatedFrom(e);
        SearchBox.Text = string.Empty;
        ViewModel.ApplySearch(null);
    }

    private void OnViewModelPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(BibleViewModel.SelectedChapter) or nameof(BibleViewModel.Chapters))
        {
            SyncChapterSelection();
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
            SyncChapterSelection();
        }
    }

    private void OnSearchTextChanged(AutoSuggestBox sender, AutoSuggestBoxTextChangedEventArgs args)
    {
        if (args.Reason != AutoSuggestionBoxTextChangeReason.UserInput)
        {
            return;
        }

        if (string.IsNullOrWhiteSpace(sender.Text))
        {
            ViewModel.ApplySearch(null);
        }
    }

    private void OnSearchResultSelected(object sender, SelectionChangedEventArgs e)
    {
        if (sender is ListView { SelectedItem: BibleVerseItem item })
        {
            ViewModel.NavigateToSearchResult(item);
            SyncChapterSelection();
        }
    }
}
