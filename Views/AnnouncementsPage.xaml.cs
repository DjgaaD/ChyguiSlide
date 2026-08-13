using System;
using System.Threading.Tasks;
using ChyguiSlide.Data.Entities;
using ChyguiSlide.Services;
using ChyguiSlide.ViewModels;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace ChyguiSlide.Views;

public sealed partial class AnnouncementsPage : Page
{
    public AnnouncementsViewModel ViewModel { get; }
    private AnnouncementEditorViewModel? _editorViewModel;
    private ContentDialog? _editorDialog;

    public AnnouncementsPage()
    {
        InitializeComponent();
        ViewModel = App.AppHost.Services.GetRequiredService<AnnouncementsViewModel>();
        DataContext = ViewModel;
    }

    private async void OnPageLoaded(object sender, RoutedEventArgs e)
    {
        await ViewModel.InitializeAsync();
        if (string.IsNullOrWhiteSpace(SearchBox.Text) && !string.IsNullOrWhiteSpace(ViewModel.SearchTerm))
        {
            await ViewModel.SearchCommand.ExecuteAsync(null);
        }
        else if (!string.IsNullOrWhiteSpace(ViewModel.SearchTerm))
        {
            SearchBox.Text = ViewModel.SearchTerm;
        }
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

    private async void OnCreateAnnouncementClicked(object sender, RoutedEventArgs e)
    {
        await OpenEditorDialogAsync(createNew: true);
    }

    private async void OnEditAnnouncementClicked(object sender, RoutedEventArgs e)
    {
        await OpenEditorDialogAsync(createNew: false);
    }

    private async void OnDeleteAnnouncementClicked(object sender, RoutedEventArgs e)
    {
        if (ViewModel.SelectedAnnouncement is not Announcement selected)
        {
            return;
        }

        var dialog = new ContentDialog
        {
            Title = "Удаление объявления",
            Content = $"Удалить «{selected.Title}»?",
            PrimaryButtonText = "Удалить",
            CloseButtonText = "Отмена",
            DefaultButton = ContentDialogButton.Close,
            XamlRoot = XamlRoot
        };

        if (await ContentDialogTheme.ShowAsync(dialog) == ContentDialogResult.Primary)
        {
            await ViewModel.DeleteAsync(selected);
        }
    }

    private void OnAddToQuickPlaylistClicked(object sender, RoutedEventArgs e)
    {
        ViewModel.AddToQuickPlaylistCommand.Execute(null);
    }

    private async void OnQuickAnnouncementClicked(object sender, RoutedEventArgs e)
    {
        var typeCombo = new ComboBox
        {
            Header = "Тип объявления",
            HorizontalAlignment = HorizontalAlignment.Stretch,
            ItemsSource = new[]
            {
                new QuickAnnouncementTypeOption("manual", "Ручное"),
                new QuickAnnouncementTypeOption("remove_car", "Убрать автомобиль")
            },
            DisplayMemberPath = nameof(QuickAnnouncementTypeOption.Title),
            SelectedIndex = 0
        };

        var plateBox = new TextBox
        {
            Header = "Госномер",
            PlaceholderText = "А587АА 761",
            HorizontalAlignment = HorizontalAlignment.Stretch,
            Visibility = Visibility.Collapsed
        };

        var contentBox = new TextBox
        {
            Header = "Текст",
            PlaceholderText = "Текст объявления. Пустая строка — следующий слайд.",
            AcceptsReturn = true,
            TextWrapping = TextWrapping.Wrap,
            MinHeight = 160,
            HorizontalAlignment = HorizontalAlignment.Stretch
        };

        var previewText = new TextBlock
        {
            TextWrapping = TextWrapping.Wrap,
            Visibility = Visibility.Collapsed,
            Foreground = ThemeBrushHelper.Get("TextFillColorPrimaryBrush")
        };

        void SyncTypeUi()
        {
            var isRemoveCar = typeCombo.SelectedItem is QuickAnnouncementTypeOption { Id: "remove_car" };
            plateBox.Visibility = isRemoveCar ? Visibility.Visible : Visibility.Collapsed;
            contentBox.Visibility = isRemoveCar ? Visibility.Collapsed : Visibility.Visible;
            previewText.Visibility = isRemoveCar ? Visibility.Visible : Visibility.Collapsed;

            if (isRemoveCar)
            {
                UpdateRemoveCarPreview();
            }
        }

        void UpdateRemoveCarPreview()
        {
            var plate = (plateBox.Text ?? string.Empty).Trim();
            previewText.Text = string.IsNullOrWhiteSpace(plate)
                ? "Просьба убрать автомобиль госномер: …"
                : $"Просьба убрать автомобиль госномер: {plate}";
        }

        typeCombo.SelectionChanged += (_, _) => SyncTypeUi();

        plateBox.BeforeTextChanging += (sender, args) =>
        {
            var upper = args.NewText.ToUpperInvariant();
            if (string.Equals(args.NewText, upper, StringComparison.Ordinal))
            {
                return;
            }

            // Cancel не даёт маленькой букве отрисоваться; uppercase ставим на следующем тике.
            args.Cancel = true;
            var selectionStart = sender.SelectionStart;
            var selectionLength = sender.SelectionLength;
            var oldLength = (sender.Text ?? string.Empty).Length;
            var insertedLength = upper.Length - (oldLength - selectionLength);
            var caret = Math.Clamp(selectionStart + insertedLength, 0, upper.Length);

            sender.DispatcherQueue.TryEnqueue(() =>
            {
                if (!string.Equals(sender.Text, upper, StringComparison.Ordinal))
                {
                    sender.Text = upper;
                }

                sender.SelectionStart = caret;
                sender.SelectionLength = 0;
            });
        };

        plateBox.TextChanged += (_, _) => UpdateRemoveCarPreview();
        SyncTypeUi();

        var panel = new StackPanel
        {
            Spacing = 12,
            MinWidth = 420,
            Children =
            {
                new TextBlock
                {
                    TextWrapping = TextWrapping.Wrap,
                    Foreground = ThemeBrushHelper.Get("TextFillColorSecondaryBrush"),
                    Text = "Сразу выводит текст на экран. Чтобы сохранить объявление — используйте «Добавить»."
                },
                typeCombo,
                plateBox,
                previewText,
                contentBox
            }
        };

        var dialog = new ContentDialog
        {
            Title = "Быстрое объявление",
            Content = panel,
            PrimaryButtonText = "Показать на экране",
            CloseButtonText = "Отмена",
            DefaultButton = ContentDialogButton.Primary,
            XamlRoot = XamlRoot
        };

        if (await ContentDialogTheme.ShowAsync(dialog) != ContentDialogResult.Primary)
        {
            return;
        }

        string content;
        if (typeCombo.SelectedItem is QuickAnnouncementTypeOption { Id: "remove_car" })
        {
            var plate = (plateBox.Text ?? string.Empty).Trim();
            if (string.IsNullOrWhiteSpace(plate))
            {
                await ContentDialogTheme.ShowAsync(new ContentDialog
                {
                    Title = "Госномер",
                    Content = "Введите госномер автомобиля, например: А587АА 761",
                    CloseButtonText = "OK",
                    XamlRoot = XamlRoot
                });
                return;
            }

            content = $"Просьба убрать автомобиль госномер: {plate}";
        }
        else
        {
            content = contentBox.Text ?? string.Empty;
        }

        await ViewModel.ShowQuickAsync(content);
    }

    private sealed record QuickAnnouncementTypeOption(string Id, string Title);

    private async Task OpenEditorDialogAsync(bool createNew)
    {
        var editorPage = new AnnouncementEditorPage
        {
            HorizontalAlignment = HorizontalAlignment.Stretch,
            VerticalAlignment = VerticalAlignment.Stretch
        };

        _editorViewModel = editorPage.ViewModel;
        _editorViewModel.AnnouncementSaved += OnAnnouncementSaved;

        if (createNew)
        {
            _editorViewModel.CreateNew();
        }
        else if (ViewModel.SelectedAnnouncement is { } announcement)
        {
            _editorViewModel.LoadFrom(announcement);
        }
        else
        {
            return;
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
            Title = createNew ? "Новое объявление" : "Редактор объявления",
            Content = host,
            PrimaryButtonText = "Сохранить",
            CloseButtonText = "Закрыть",
            DefaultButton = ContentDialogButton.Primary,
            FullSizeDesired = false,
            XamlRoot = XamlRoot
        };

        _editorDialog.Resources["ContentDialogMaxWidth"] = width + 48;
        _editorDialog.Resources["ContentDialogMinWidth"] = Math.Min(width, 640);
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
                _editorViewModel.AnnouncementSaved -= OnAnnouncementSaved;
                _editorViewModel = null;
            }

            _editorDialog = null;
        }
    }

    private async void OnEditorPrimarySaveClick(ContentDialog sender, ContentDialogButtonClickEventArgs args)
    {
        args.Cancel = true;
        if (_editorViewModel is null || !_editorViewModel.SaveCommand.CanExecute(null))
        {
            return;
        }

        var deferral = args.GetDeferral();
        try
        {
            await _editorViewModel.SaveCommand.ExecuteAsync(null);
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
            // fallback
        }

        // Одна колонка — уже диалог, без «пустого столбца» слева
        var width = Math.Clamp(windowWidth * 0.55, 520, 720);
        var height = Math.Clamp(windowHeight - 180, 480, 700);
        return (width, height);
    }

    private void OnAnnouncementSaved(object? sender, Announcement e)
    {
        _ = DispatcherQueue.TryEnqueue(async () =>
        {
            _editorDialog?.Hide();
            await ViewModel.LoadCommand.ExecuteAsync(null);
            ViewModel.SelectAnnouncement(e.Id);
        });
    }
}
