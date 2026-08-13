using System;
using ChyguiSlide.ViewModels;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Input;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Windows.System;
using Windows.UI.Core;

using ChyguiSlide.Services;

namespace ChyguiSlide.Views;

public sealed partial class ThemeEditorPage : Page
{
    public ThemePresetEditorViewModel ViewModel { get; }

    public ThemeEditorPage()
    {
        InitializeComponent();
        ViewModel = App.AppHost.Services.GetRequiredService<ThemePresetEditorViewModel>();
        DataContext = ViewModel;
        Loaded += (_, _) => AppUiThemeApplier.ApplyToElement(this);
    }

    private async void OnPickPrimaryColorClicked(object sender, RoutedEventArgs e)
        => await PickColorAsync(c => ViewModel.PrimaryPickerColor = c, ViewModel.PrimaryPickerColor);

    private async void OnPickBackgroundColorClicked(object sender, RoutedEventArgs e)
        => await PickColorAsync(c => ViewModel.BackgroundPickerColor = c, ViewModel.BackgroundPickerColor);

    private async void OnPickOutlineColorClicked(object sender, RoutedEventArgs e)
        => await PickColorAsync(c => ViewModel.TextOutlinePickerColor = c, ViewModel.TextOutlinePickerColor);

    private void OnWallpaperPreviewTapped(object sender, TappedRoutedEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: ThemeWallpaperItem item })
        {
            ViewModel.SelectWallpaperItemAsFixed(item);
        }
    }

    private void OnWallpaperPreviewPointerEntered(object sender, PointerRoutedEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: ThemeWallpaperItem item })
        {
            item.IsHovering = true;
        }
    }

    private void OnWallpaperPreviewPointerExited(object sender, PointerRoutedEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: ThemeWallpaperItem item })
        {
            item.IsHovering = false;
        }
    }

    private void OnWallpaperRenameClick(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement { DataContext: ThemeWallpaperItem item } button)
        {
            return;
        }

        item.BeginRename();

        if (button.Parent is Panel grid)
        {
            foreach (var child in grid.Children)
            {
                if (child is TextBox box)
                {
                    box.DispatcherQueue.TryEnqueue(() =>
                    {
                        box.Focus(FocusState.Programmatic);
                        box.SelectAll();
                    });
                    break;
                }
            }
        }
    }

    private async void OnWallpaperDisplayNameLostFocus(object sender, RoutedEventArgs e)
    {
        if (sender is TextBox { DataContext: ThemeWallpaperItem item } box)
        {
            item.DisplayName = box.Text ?? string.Empty;
            item.EndRename();
            await ViewModel.CommitWallpaperDisplayNameAsync(item);
        }
    }

    private async void OnWallpaperDisplayNameKeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (e.Key != VirtualKey.Enter || sender is not TextBox { DataContext: ThemeWallpaperItem item } box)
        {
            return;
        }

        e.Handled = true;
        item.DisplayName = box.Text ?? string.Empty;
        item.EndRename();
        await ViewModel.CommitWallpaperDisplayNameAsync(item);
    }

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

        if (await ContentDialogTheme.ShowAsync(dialog) == ContentDialogResult.Primary)
        {
            apply(picker.Color);
        }
    }
}
