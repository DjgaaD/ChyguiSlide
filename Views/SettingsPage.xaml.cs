using System;
using ChyguiSlide.Data.Entities;
using ChyguiSlide.Services;
using ChyguiSlide.Services.Models;
using ChyguiSlide.ViewModels;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Input;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Windows.System;
using Windows.UI.Core;
using ChyguiSlide.Views.Dialogs;

namespace ChyguiSlide.Views;

public sealed partial class SettingsPage : Page
{
    public ThemePresetEditorViewModel ViewModel { get; }
    public LogsViewModel LogsViewModel { get; }

    private ContentDialog? _themeEditorDialog;

    public SettingsPage()
    {
        ViewModel = App.AppHost.Services.GetRequiredService<ThemePresetEditorViewModel>();
        LogsViewModel = App.AppHost.Services.GetRequiredService<LogsViewModel>();
        InitializeComponent();
        DataContext = ViewModel;
        Loaded += OnLoaded;
        Unloaded += OnUnloaded;
        AddHandler(KeyDownEvent, new KeyEventHandler(OnPageKeyDown), handledEventsToo: true);
    }

    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        Loaded -= OnLoaded;
        try
        {
            ViewModel.XamlRoot = XamlRoot;
            if (SettingsNav.SelectedItem is null && ViewModel.SelectedSettingsSection is not null)
            {
                SettingsNav.SelectedItem = ViewModel.SelectedSettingsSection;
            }

            await ViewModel.InitializeAsync();
        }
        catch (Exception ex)
        {
            ViewModel.StatusMessage = null;
            await ErrorDialog.ShowAsync(XamlRoot, "Не удалось загрузить настройки", ex);
        }
    }

    private void OnUnloaded(object sender, RoutedEventArgs e)
    {
        ViewModel.CancelHotkeyListening();
    }

    private void OnSettingsSectionChanged(NavigationView sender, NavigationViewSelectionChangedEventArgs args)
    {
        if (args.SelectedItem is SettingsNavItem item)
        {
            ViewModel.SelectedSettingsSection = item;
        }
        else if (args.SelectedItemContainer?.DataContext is SettingsNavItem fromContainer)
        {
            ViewModel.SelectedSettingsSection = fromContainer;
        }
    }

    private void OnTextLayoutOptionClick(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { Tag: TextLayoutMode mode })
        {
            ViewModel.SelectTextLayoutModeCommand.Execute(mode);
        }
    }

    private async void OnCreatePresetClicked(object sender, RoutedEventArgs e)
    {
        await OpenThemeEditorDialogAsync(createNew: true);
    }

    private void OnPresetPreviewTapped(object sender, TappedRoutedEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: ThemePresetListItem item })
        {
            ViewModel.SelectPresetItem(item);
        }
    }

    private void OnPresetPreviewPointerEntered(object sender, PointerRoutedEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: ThemePresetListItem item })
        {
            item.IsHovering = true;
        }
    }

    private void OnPresetPreviewPointerExited(object sender, PointerRoutedEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: ThemePresetListItem item })
        {
            item.IsHovering = false;
        }
    }

    private async void OnPresetEditClick(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement { DataContext: ThemePresetListItem item })
        {
            return;
        }

        await OpenThemeEditorDialogAsync(createNew: false, item.Preset);
    }

    private async System.Threading.Tasks.Task OpenThemeEditorDialogAsync(bool createNew, ThemePreset? preset = null)
    {
        if (createNew)
        {
            ViewModel.PrepareCreatePreset();
        }
        else if (preset is not null)
        {
            ViewModel.PrepareEditPreset(preset);
        }
        else
        {
            return;
        }

        var (width, height) = GetThemeEditorDialogSize();
        var editorPage = new ThemeEditorPage
        {
            Width = width,
            Height = height,
            MaxHeight = height,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            VerticalAlignment = VerticalAlignment.Stretch
        };

        ViewModel.ThemePresetSaved += OnThemePresetSaved;
        ViewModel.ThemePresetDeleted += OnThemePresetDeleted;

        // Фиксированный viewport: иначе ContentDialog даёт Page бесконечную высоту и ScrollViewer не скроллится
        var host = new Border
        {
            Width = width,
            Height = height,
            MaxHeight = height,
            Child = editorPage,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            VerticalAlignment = VerticalAlignment.Stretch
        };
        AppUiThemeApplier.ApplyToElement(host);

        _themeEditorDialog = new ContentDialog
        {
            Title = createNew ? "Новый стиль" : "Редактор стиля",
            Content = host,
            PrimaryButtonText = "Сохранить",
            CloseButtonText = "Закрыть",
            DefaultButton = ContentDialogButton.Primary,
            FullSizeDesired = false,
            XamlRoot = XamlRoot
        };

        if (!createNew)
        {
            _themeEditorDialog.SecondaryButtonText = "Удалить";
        }

        _themeEditorDialog.Resources["ContentDialogMaxWidth"] = width + 48;
        _themeEditorDialog.Resources["ContentDialogMinWidth"] = Math.Min(width, 960);
        _themeEditorDialog.Resources["ContentDialogMaxHeight"] = height + 120;

        _themeEditorDialog.PrimaryButtonClick += OnThemeEditorPrimarySaveClick;
        if (!createNew)
        {
            _themeEditorDialog.SecondaryButtonClick += OnThemeEditorSecondaryDeleteClick;
        }

        try
        {
            await ContentDialogTheme.ShowAsync(_themeEditorDialog);
        }
        finally
        {
            _themeEditorDialog.PrimaryButtonClick -= OnThemeEditorPrimarySaveClick;
            _themeEditorDialog.SecondaryButtonClick -= OnThemeEditorSecondaryDeleteClick;
            ViewModel.ThemePresetSaved -= OnThemePresetSaved;
            ViewModel.ThemePresetDeleted -= OnThemePresetDeleted;
            ViewModel.EndModalEdit();
            _themeEditorDialog = null;
        }
    }

    private async void OnThemeEditorPrimarySaveClick(ContentDialog sender, ContentDialogButtonClickEventArgs args)
    {
        args.Cancel = true;
        if (!ViewModel.SaveCommand.CanExecute(null))
        {
            return;
        }

        var deferral = args.GetDeferral();
        try
        {
            await ViewModel.SaveCommand.ExecuteAsync(null);
        }
        finally
        {
            deferral.Complete();
        }
    }

    private async void OnThemeEditorSecondaryDeleteClick(ContentDialog sender, ContentDialogButtonClickEventArgs args)
    {
        args.Cancel = true;
        if (!ViewModel.DeleteCommand.CanExecute(null))
        {
            return;
        }

        var deferral = args.GetDeferral();
        try
        {
            await ViewModel.DeleteCommand.ExecuteAsync(null);
        }
        finally
        {
            deferral.Complete();
        }
    }

    private void OnThemePresetSaved(object? sender, ThemePreset e)
    {
        _ = DispatcherQueue.TryEnqueue(() => _themeEditorDialog?.Hide());
    }

    private void OnThemePresetDeleted(object? sender, EventArgs e)
    {
        _ = DispatcherQueue.TryEnqueue(() => _themeEditorDialog?.Hide());
    }

    private static (double Width, double Height) GetThemeEditorDialogSize()
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

        var width = Math.Clamp(windowWidth - 96, 900, 1400);
        var height = Math.Clamp(windowHeight - 160, 560, 900);
        return (width, height);
    }

    private void OnPageKeyDown(object sender, KeyRoutedEventArgs e)
    {
        var ctrl = IsDown(VirtualKey.Control);
        var alt = IsDown(VirtualKey.Menu);
        var shift = IsDown(VirtualKey.Shift);

        if (ViewModel.TryCaptureHotkey(e.Key, ctrl, alt, shift))
        {
            e.Handled = true;
        }
    }

    private static bool IsDown(VirtualKey key)
        => InputKeyboardSource.GetKeyStateForCurrentThread(key).HasFlag(CoreVirtualKeyStates.Down);

    private async void OnRestoreYandexBackupClicked(object sender, RoutedEventArgs e)
    {
        if (ViewModel.SelectedYandexBackup is null)
        {
            ViewModel.BackupStatusMessage = "Сначала выберите копию в списке.";
            return;
        }

        var name = ViewModel.SelectedYandexBackup.Name;
        var dialog = new ContentDialog
        {
            Title = "Восстановление базы",
            Content =
                $"Заменить текущую базу песен копией «{name}»?\n\n" +
                "Текущая база будет сохранена рядом как catalog.before-restore-….db.\n" +
                "После восстановления нужно перезапустить приложение.",
            PrimaryButtonText = "Восстановить",
            CloseButtonText = "Отмена",
            DefaultButton = ContentDialogButton.Close,
            XamlRoot = XamlRoot
        };

        if (await ContentDialogTheme.ShowAsync(dialog) == ContentDialogResult.Primary)
        {
            await ViewModel.RestoreYandexBackupCommand.ExecuteAsync(null);
        }
    }

}
