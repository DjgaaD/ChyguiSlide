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
    private static bool _initializedOnce;

    public AnnouncementsPage()
    {
        InitializeComponent();
        ViewModel = App.AppHost.Services.GetRequiredService<AnnouncementsViewModel>();
        DataContext = ViewModel;
    }

    private async void OnPageLoaded(object sender, RoutedEventArgs e)
    {
        // Инициализируем только один раз при первом запуске
        if (!_initializedOnce)
        {
            _initializedOnce = true;
            await ViewModel.InitializeAsync();
        }
        // Не синхронизируем поиск при загрузке, чтобы избежать мигания при навигации
    }

    protected override void OnNavigatedFrom(Microsoft.UI.Xaml.Navigation.NavigationEventArgs e)
    {
        base.OnNavigatedFrom(e);
        // Не очищаем поиск при уходе со страницы, чтобы избежать мигания при навигации
        // SearchBox.Text = string.Empty;
        // _ = ViewModel.SearchCommand.ExecuteAsync(null);
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
        var manualRadio = new RadioButton
        {
            GroupName = "AnnouncementType",
            Content = "Ручное",
            IsChecked = true
        };

        var removeCarRadio = new RadioButton
        {
            GroupName = "AnnouncementType",
            Content = "Убрать автомобиль"
        };

        // Применяем стиль SegmentedRadioStyle если он доступен
        if (Application.Current.Resources.TryGetValue("SegmentedRadioStyle", out var radioStyle))
        {
            manualRadio.Style = radioStyle as Style;
            removeCarRadio.Style = radioStyle as Style;
        }

        var typeGrid = new Grid
        {
            ColumnDefinitions =
            {
                new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) },
                new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) }
            }
        };

        Grid.SetColumn(manualRadio, 0);
        Grid.SetColumn(removeCarRadio, 1);

        typeGrid.Children.Add(manualRadio);
        typeGrid.Children.Add(removeCarRadio);

        var segmentedTrack = new Border();
        if (Application.Current.Resources.TryGetValue("SegmentedTrackStyle", out var trackStyle))
        {
            segmentedTrack.Style = trackStyle as Style;
        }
        segmentedTrack.Child = typeGrid;

        var typeHeader = new TextBlock
        {
            Text = "Тип объявления",
            Foreground = ThemeBrushHelper.Get("TextFillColorSecondaryBrush"),
            Margin = new Thickness(0, 0, 0, 8)
        };

        var typeContainer = new StackPanel
        {
            Spacing = 8,
            Children = { typeHeader, segmentedTrack }
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
            var isRemoveCar = removeCarRadio.IsChecked == true;
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

        manualRadio.Checked += (_, _) => SyncTypeUi();
        removeCarRadio.Checked += (_, _) => SyncTypeUi();

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

        // Используем Grid с фиксированной высотой, чтобы окно не меняло размер при переключении
        var grid = new Grid
        {
            RowDefinitions =
            {
                new RowDefinition { Height = GridLength.Auto },
                new RowDefinition { Height = GridLength.Auto },
                new RowDefinition { Height = GridLength.Auto },
                new RowDefinition { Height = GridLength.Auto },
                new RowDefinition { Height = new GridLength(1, GridUnitType.Star) }
            }
        };

        var infoText = new TextBlock
        {
            TextWrapping = TextWrapping.Wrap,
            Foreground = ThemeBrushHelper.Get("TextFillColorSecondaryBrush"),
            Text = "Сразу выводит текст на экран. Чтобы сохранить объявление — используйте «Добавить»."
        };
        Grid.SetRow(infoText, 0);

        Grid.SetRow(typeContainer, 1);
        Grid.SetRow(plateBox, 2);
        Grid.SetRow(previewText, 3);
        Grid.SetRow(contentBox, 4);

        grid.Children.Add(infoText);
        grid.Children.Add(typeContainer);
        grid.Children.Add(plateBox);
        grid.Children.Add(previewText);
        grid.Children.Add(contentBox);

        var panel = new StackPanel
        {
            Spacing = 12,
            MinWidth = 420,
            MinHeight = 320,
            Children = { grid }
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
        if (removeCarRadio.IsChecked == true)
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
