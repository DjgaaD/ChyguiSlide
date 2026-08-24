using System;
using ChyguiSlide.Services;
using ChyguiSlide.ViewModels;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Windows.System;

namespace ChyguiSlide.Views;

public sealed partial class ThemeEditorPage : Page
{
    private Action<global::Windows.UI.Color>? _pendingColorApply;

    public ThemePresetEditorViewModel ViewModel { get; }

    public ThemeEditorPage()
    {
        InitializeComponent();
        ViewModel = App.AppHost.Services.GetRequiredService<ThemePresetEditorViewModel>();
        DataContext = ViewModel;
        Loaded += (_, _) => AppUiThemeApplier.ApplyToElement(this);
    }

    private void OnPickPrimaryColorClicked(object sender, RoutedEventArgs e)
        => OpenColorPicker("Цвет текста", ViewModel.PrimaryPickerColor, c => ViewModel.PrimaryPickerColor = c);

    private void OnPickBackgroundColorClicked(object sender, RoutedEventArgs e)
        => OpenColorPicker("Цвет фона", ViewModel.BackgroundPickerColor, c => ViewModel.BackgroundPickerColor = c);

    private void OnPickOutlineColorClicked(object sender, RoutedEventArgs e)
        => OpenColorPicker("Цвет обводки", ViewModel.TextOutlinePickerColor, c => ViewModel.TextOutlinePickerColor = c);

    private void OpenColorPicker(
        string title,
        global::Windows.UI.Color initial,
        Action<global::Windows.UI.Color> apply)
    {
        _pendingColorApply = apply;
        ColorPickerTitle.Text = title;
        InlineColorPicker.Color = initial;
        ColorPickerOverlay.Visibility = Visibility.Visible;
    }

    private void OnColorPickerOkClicked(object sender, RoutedEventArgs e)
    {
        _pendingColorApply?.Invoke(InlineColorPicker.Color);
        CloseColorPicker();
    }

    private void OnColorPickerCancelClicked(object sender, RoutedEventArgs e)
        => CloseColorPicker();

    private void CloseColorPicker()
    {
        ColorPickerOverlay.Visibility = Visibility.Collapsed;
        _pendingColorApply = null;
    }

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
}
