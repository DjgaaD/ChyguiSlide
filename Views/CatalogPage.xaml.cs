using System;
using System.Linq;
using ChyguiSlide.Data.Entities;
using ChyguiSlide.Services;
using ChyguiSlide.ViewModels;
using ChyguiSlide.Views.Dialogs;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml;
using System.Threading.Tasks;
using Windows.Storage.Pickers;
using WinRT.Interop;

namespace ChyguiSlide.Views;

public sealed partial class CatalogPage : Page
{
    public CatalogViewModel ViewModel { get; }
    private SongEditorViewModel? _editorViewModel;
    private ContentDialog? _editorDialog;

    public CatalogPage()
    {
        InitializeComponent();
        ViewModel = App.AppHost.Services.GetRequiredService<CatalogViewModel>();
        DataContext = ViewModel;
        ViewModel.SearchFocusRequested += OnSearchFocusRequested;
    }

    private async void OnPageLoaded(object sender, RoutedEventArgs e)
    {
        await ViewModel.InitializeAsync();
        // Поле поиска в новой странице пустое — синхронизируем фильтр, иначе остаются старые результаты
        if (string.IsNullOrWhiteSpace(SearchBox.Text) && !string.IsNullOrWhiteSpace(ViewModel.SearchTerm))
        {
            await ViewModel.SearchCommand.ExecuteAsync(null);
        }
        else if (!string.IsNullOrWhiteSpace(ViewModel.SearchTerm))
        {
            SearchBox.Text = ViewModel.SearchTerm;
        }

        await FocusSearchBoxIfRequestedAsync();
    }

    protected override void OnNavigatedFrom(Microsoft.UI.Xaml.Navigation.NavigationEventArgs e)
    {
        base.OnNavigatedFrom(e);
        SearchBox.Text = string.Empty;
        _ = ViewModel.SearchCommand.ExecuteAsync(null);
    }

    private async void OnSearchBoxQuerySubmitted(AutoSuggestBox sender, AutoSuggestBoxQuerySubmittedEventArgs args)
    {
        await ViewModel.SearchCommand.ExecuteAsync(args.QueryText);
    }

    private async void OnSearchBoxTextChanged(AutoSuggestBox sender, AutoSuggestBoxTextChangedEventArgs args)
    {
        if (args.Reason != AutoSuggestionBoxTextChangeReason.UserInput)
        {
            return;
        }

        var query = string.IsNullOrWhiteSpace(sender.Text) ? null : sender.Text;
        await ViewModel.SearchCommand.ExecuteAsync(query);
    }

    private async void OnCreateSongClicked(object sender, RoutedEventArgs e)
    {
        await OpenEditorDialogAsync(createNew: true);
    }

    private async void OnImportSongClicked(object sender, RoutedEventArgs e)
    {
        await OpenEditorDialogAsync(createNew: true, async vm =>
        {
            if (vm.ImportPresentationCommand.CanExecute(null))
            {
                await vm.ImportPresentationCommand.ExecuteAsync(null);
            }
        });
    }

    private async void OnImportSpsClicked(object sender, RoutedEventArgs e)
    {
        var picker = new FileOpenPicker
        {
            SuggestedStartLocation = PickerLocationId.Downloads,
            ViewMode = PickerViewMode.List
        };
        picker.FileTypeFilter.Add(".sps");
        InitializeWithWindow.Initialize(picker, App.MainWindowHandle);

        var file = await picker.PickSingleFileAsync();
        if (file is null)
        {
            return;
        }

        var statusText = new TextBlock
        {
            Text = "Чтение файла…",
            TextWrapping = TextWrapping.Wrap
        };
        var progressBar = new ProgressBar
        {
            Minimum = 0,
            Maximum = 100,
            IsIndeterminate = true,
            Height = 8
        };

        var progressDialog = new ContentDialog
        {
            Title = "Импорт SoftProjector",
            Content = new StackPanel
            {
                Spacing = 12,
                MinWidth = 360,
                Children = { statusText, progressBar }
            },
            XamlRoot = XamlRoot
        };

        var progress = new Progress<SpsImportProgress>(p =>
        {
            statusText.Text = p.Message;
            if (p.Total > 0)
            {
                progressBar.IsIndeterminate = false;
                progressBar.Maximum = p.Total;
                progressBar.Value = p.Done;
            }
        });

        var showTask = ContentDialogTheme.ShowAsync(progressDialog);
        SpsImportSummary summary;
        try
        {
            summary = await ViewModel.ImportSpsAsync(file.Path, progress);
        }
        catch (Exception ex)
        {
            progressDialog.Hide();
            await showTask;
            await ErrorDialog.ShowAsync(XamlRoot, "Ошибка импорта SPS", ex);
            return;
        }

        progressDialog.Hide();
        await showTask;

        var message = $"Сборник «{summary.SongbookName}».\nИмпортировано: {summary.Imported}.\nПропущено: {summary.Skipped}.";
        if (!string.IsNullOrWhiteSpace(summary.Warning))
        {
            message += $"\n\n{summary.Warning}";
        }

        var doneDialog = new ContentDialog
        {
            Title = "Импорт завершён",
            Content = message,
            CloseButtonText = "OK",
            XamlRoot = XamlRoot
        };
        await ContentDialogTheme.ShowAsync(doneDialog);
    }

    private async void OnImportFromUrlClicked(object sender, RoutedEventArgs e)
    {
        var urlBox = new TextBox
        {
            PlaceholderText = "https://…",
            MinWidth = 420
        };

        var dialog = new ContentDialog
        {
            Title = "Импорт песни с сайта",
            Content = new StackPanel
            {
                Spacing = 12,
                Children =
                {
                    new TextBlock
                    {
                        TextWrapping = TextWrapping.Wrap,
                        Text = "Вставьте ссылку на страницу с текстом песни. Программа попытается найти куплеты и припевы автоматически — качество зависит от сайта."
                    },
                    urlBox
                }
            },
            PrimaryButtonText = "Импортировать",
            CloseButtonText = "Отмена",
            DefaultButton = ContentDialogButton.Primary,
            XamlRoot = XamlRoot
        };

        var result = await ContentDialogTheme.ShowAsync(dialog);
        if (result != ContentDialogResult.Primary || string.IsNullOrWhiteSpace(urlBox.Text))
        {
            return;
        }

        try
        {
            await OpenEditorDialogAsync(createNew: true, async vm =>
            {
                await vm.ImportFromUrlAsync(urlBox.Text.Trim());
            });
        }
        catch (Exception ex)
        {
            await ErrorDialog.ShowAsync(XamlRoot, "Ошибка импорта", ex);
        }
    }

    private async void OnCreateCollectionClicked(object sender, RoutedEventArgs e)
    {
        var nameBox = new TextBox
        {
            PlaceholderText = "Например: Песнь возрождения 3300",
            MinWidth = 360
        };

        var dialog = new ContentDialog
        {
            Title = "Новый сборник",
            Content = nameBox,
            PrimaryButtonText = "Создать",
            CloseButtonText = "Отмена",
            DefaultButton = ContentDialogButton.Primary,
            XamlRoot = XamlRoot
        };

        var result = await ContentDialogTheme.ShowAsync(dialog);
        if (result == ContentDialogResult.Primary && !string.IsNullOrWhiteSpace(nameBox.Text))
        {
            await ViewModel.CreateCollectionCommand.ExecuteAsync(nameBox.Text.Trim());
        }
    }

    private async void OnDeleteCollectionClicked(object sender, RoutedEventArgs e)
    {
        if (!ViewModel.DeleteSelectedCollectionCommand.CanExecute(null))
        {
            return;
        }

        var name = ViewModel.SelectedCollectionFilter?.Title ?? "сборник";
        var dialog = new ContentDialog
        {
            Title = "Удаление сборника",
            Content = $"Удалить сборник «{name}» вместе со всеми песнями в нём? Это нельзя отменить.",
            PrimaryButtonText = "Удалить",
            CloseButtonText = "Отмена",
            XamlRoot = XamlRoot
        };

        if (await ContentDialogTheme.ShowAsync(dialog) == ContentDialogResult.Primary)
        {
            await ViewModel.DeleteSelectedCollectionCommand.ExecuteAsync(null);
        }
    }

    private async void OnExpandChorusInCollectionClicked(object sender, RoutedEventArgs e)
    {
        if (!ViewModel.ExpandChorusInCollectionCommand.CanExecute(null))
        {
            var tip = new ContentDialog
            {
                Title = "Припев после куплетов",
                Content = "Сначала выберите сборник в фильтре (не «Все песни»).",
                CloseButtonText = "OK",
                XamlRoot = XamlRoot
            };
            await ContentDialogTheme.ShowAsync(tip);
            return;
        }

        var name = ViewModel.SelectedCollectionFilter?.Title ?? "сборник";
        var confirm = new ContentDialog
        {
            Title = "Припев после куплетов",
            Content = $"Во всех песнях сборника «{name}» вставить припев после каждого куплета?\nУже стоящие припевы не дублируются.",
            PrimaryButtonText = "Применить",
            CloseButtonText = "Отмена",
            XamlRoot = XamlRoot
        };

        if (await ContentDialogTheme.ShowAsync(confirm) != ContentDialogResult.Primary)
        {
            return;
        }

        var statusText = new TextBlock { Text = "Обработка…", TextWrapping = TextWrapping.Wrap };
        var progressBar = new ProgressBar { Minimum = 0, Maximum = 100, IsIndeterminate = true, Height = 8 };
        var progressDialog = new ContentDialog
        {
            Title = "Припев после куплетов",
            Content = new StackPanel { Spacing = 12, MinWidth = 360, Children = { statusText, progressBar } },
            XamlRoot = XamlRoot
        };

        var progress = new Progress<SpsImportProgress>(p =>
        {
            statusText.Text = p.Message;
            if (p.Total > 0)
            {
                progressBar.IsIndeterminate = false;
                progressBar.Maximum = p.Total;
                progressBar.Value = p.Done;
            }
        });

        var showTask = ContentDialogTheme.ShowAsync(progressDialog);
        (int songsChanged, int chorusesInserted) result;
        try
        {
            result = await ViewModel.ExpandChorusInSelectedCollectionAsync(progress);
        }
        catch (Exception ex)
        {
            progressDialog.Hide();
            await showTask;
            await ErrorDialog.ShowAsync(XamlRoot, "Ошибка", ex);
            return;
        }

        progressDialog.Hide();
        await showTask;

        var done = new ContentDialog
        {
            Title = "Готово",
            Content = $"Изменено песен: {result.songsChanged}.\nВставлено припевов: {result.chorusesInserted}.",
            CloseButtonText = "OK",
            XamlRoot = XamlRoot
        };
        await ContentDialogTheme.ShowAsync(done);
    }

    private async void OnEditSongClicked(object sender, RoutedEventArgs e)
    {
        if (ViewModel.SelectedSong is null)
        {
            return;
        }

        await OpenEditorDialogAsync(createNew: false);
    }

    private async System.Threading.Tasks.Task OpenEditorDialogAsync(
        bool createNew,
        Func<SongEditorViewModel, System.Threading.Tasks.Task>? prepareAsync = null)
    {
        var editorPage = new EditorPage
        {
            HorizontalAlignment = HorizontalAlignment.Stretch,
            VerticalAlignment = VerticalAlignment.Stretch
        };
        _editorViewModel = editorPage.ViewModel;
        _editorViewModel.SongSaved += OnSongSaved;

        await _editorViewModel.RefreshCollectionOptionsAsync();

        if (createNew)
        {
            // Сначала сброс, потом импорт (prepareAsync) — иначе Loaded/старое состояние могло подставить другую песню
            _editorViewModel.CreateNewSongCommand.Execute(null);
        }
        else if (ViewModel.SelectedSong is { } song)
        {
            // Грузим полную песню с секциями, если в списке каталога секций нет
            var full = await App.AppHost.Services
                .GetRequiredService<ChyguiSlide.Services.Abstractions.ICatalogService>()
                .GetSongAsync(song.Id) ?? song;
            _editorViewModel.SelectedSong = full;
        }

        if (prepareAsync is not null)
        {
            await prepareAsync(_editorViewModel);
        }

        var (width, height) = GetEditorDialogSize();
        var host = new Border
        {
            Child = editorPage,
            Width = width,
            Height = height,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            VerticalAlignment = VerticalAlignment.Stretch
        };
        AppUiThemeApplier.ApplyToElement(host);

        _editorDialog = new ContentDialog
        {
            Title = createNew ? "Новая песня" : "Редактор песни",
            Content = host,
            PrimaryButtonText = "Сохранить",
            CloseButtonText = "Закрыть",
            DefaultButton = ContentDialogButton.Primary,
            FullSizeDesired = true,
            XamlRoot = XamlRoot
        };

        // WinUI по умолчанию режет диалог ~548px — без этого FullSizeDesired почти не помогает
        _editorDialog.Resources["ContentDialogMaxWidth"] = width + 48;
        _editorDialog.Resources["ContentDialogMinWidth"] = Math.Min(width, 960);
        _editorDialog.Resources["ContentDialogMaxHeight"] = height + 120;

        _editorDialog.PrimaryButtonClick += OnEditorPrimarySaveClick;

        try
        {
            await ContentDialogTheme.ShowAsync(_editorDialog);
        }
        finally
        {
            _editorDialog.PrimaryButtonClick -= OnEditorPrimarySaveClick;
            if (_editorViewModel is not null)
            {
                _editorViewModel.SongSaved -= OnSongSaved;
                _editorViewModel = null;
            }

            _editorDialog = null;
        }
    }

    private async void OnEditorPrimarySaveClick(ContentDialog sender, ContentDialogButtonClickEventArgs args)
    {
        // Не закрывать диалог сразу — закроем только после успешного Save (OnSongSaved)
        args.Cancel = true;
        if (_editorViewModel is null || !_editorViewModel.SaveSongCommand.CanExecute(null))
        {
            return;
        }

        var deferral = args.GetDeferral();
        try
        {
            await _editorViewModel.SaveSongCommand.ExecuteAsync(null);
        }
        finally
        {
            deferral.Complete();
        }
    }

    private static (double Width, double Height) GetEditorDialogSize()
    {
        double windowWidth = 1280;
        double windowHeight = 800;
        try
        {
            if (App.MainWindow is not null)
            {
                var bounds = App.MainWindow.Bounds;
                if (bounds.Width > 200 && bounds.Height > 200)
                {
                    windowWidth = bounds.Width;
                    windowHeight = bounds.Height;
                }
            }
        }
        catch
        {
            // fallback ниже
        }

        var width = Math.Clamp(windowWidth - 96, 900, 1400);
        var height = Math.Clamp(windowHeight - 160, 560, 900);
        return (width, height);
    }

    private void OnSongSaved(object? sender, Song e)
    {
        _ = DispatcherQueue.TryEnqueue(async () =>
        {
            _editorDialog?.Hide();
            await ViewModel.RefreshCollectionFiltersAsync(e.CollectionId);
            await ViewModel.LoadSongsCommand.ExecuteAsync(null);
            ViewModel.SelectedSong = ViewModel.Songs.FirstOrDefault(s => s.Id == e.Id) ?? e;
        });
    }

    private void OnSortByTitleClicked(object sender, Microsoft.UI.Xaml.RoutedEventArgs e)
    {
        _ = ViewModel.SortByTitleCommand.ExecuteAsync(null);
    }

    private void OnSortByNumberClicked(object sender, Microsoft.UI.Xaml.RoutedEventArgs e)
    {
        _ = ViewModel.SortByNumberCommand.ExecuteAsync(null);
    }

    private async void OnDeleteSongClicked(object sender, RoutedEventArgs e)
    {
        if (ViewModel.SelectedSong is null)
        {
            return;
        }

        var dialog = new ContentDialog
        {
            Title = "Удаление песни",
            Content = "Точно удалить?",
            PrimaryButtonText = "Удалить",
            SecondaryButtonText = "Отмена",
            XamlRoot = XamlRoot
        };

        var result = await ContentDialogTheme.ShowAsync(dialog);
        if (result == ContentDialogResult.Primary)
        {
            await ViewModel.DeleteSongCommand.ExecuteAsync(null);
        }
    }

    private void OnAddToQuickPlaylistClicked(object sender, RoutedEventArgs e)
    {
        ViewModel.AddToQuickPlaylistCommand.Execute(null);
    }

    private async Task FocusSearchBoxIfRequestedAsync()
    {
        if (!ViewModel.ConsumePendingSearchFocusRequest())
        {
            return;
        }

        await Task.Yield();
        SearchBox.Focus(FocusState.Programmatic);
    }

    private async void OnSearchFocusRequested()
    {
        await FocusSearchBoxIfRequestedAsync();
    }
}
