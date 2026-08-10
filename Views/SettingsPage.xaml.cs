using System;
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

    public SettingsPage()
    {
        InitializeComponent();
        ViewModel = App.AppHost.Services.GetRequiredService<ThemePresetEditorViewModel>();
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

    private async void OnPickPrimaryColorClicked(object sender, RoutedEventArgs e)
        => await PickColorAsync(c => ViewModel.PrimaryPickerColor = c, ViewModel.PrimaryPickerColor);

    private async void OnPickBackgroundColorClicked(object sender, RoutedEventArgs e)
        => await PickColorAsync(c => ViewModel.BackgroundPickerColor = c, ViewModel.BackgroundPickerColor);

    private async void OnPickOutlineColorClicked(object sender, RoutedEventArgs e)
        => await PickColorAsync(c => ViewModel.TextOutlinePickerColor = c, ViewModel.TextOutlinePickerColor);

    private async System.Threading.Tasks.Task PickColorAsync(
        Action<global::Windows.UI.Color> apply,
        global::Windows.UI.Color initial)
    {
        var picker = new ColorPicker
        {
            ColorSpectrumShape = ColorSpectrumShape.Box,
            IsAlphaEnabled = false,
            IsColorChannelTextInputVisible = true,
            IsHexInputVisible = true,
            Color = initial
        };

        var dialog = new ContentDialog
        {
            Title = "Цвет",
            Content = picker,
            PrimaryButtonText = "OK",
            CloseButtonText = "Отмена",
            DefaultButton = ContentDialogButton.Primary,
            XamlRoot = XamlRoot
        };

        if (await dialog.ShowAsync() == ContentDialogResult.Primary)
        {
            apply(picker.Color);
        }
    }

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

        if (await dialog.ShowAsync() == ContentDialogResult.Primary)
        {
            await ViewModel.RestoreYandexBackupCommand.ExecuteAsync(null);
        }
    }
}
