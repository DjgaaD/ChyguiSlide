using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using ChyguiSlide.Data.Entities;
using ChyguiSlide.Services;
using ChyguiSlide.ViewModels;
using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using Windows.Storage;
using Windows.Storage.FileProperties;
using Windows.UI.Text;

namespace ChyguiSlide.Controls;

public sealed partial class ThemePresetPreviewCard : UserControl
{
    private static readonly HashSet<string> VideoExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".mp4", ".m4v", ".mov", ".wmv", ".mkv", ".avi", ".webm"
    };

    private static readonly (double X, double Y)[] OutlineOffsets =
    {
        (-1, -1), (0, -1), (1, -1),
        (-1, 0),           (1, 0),
        (-1, 1),  (0, 1),  (1, 1)
    };

    private CancellationTokenSource? _loadCts;
    private Guid? _appliedPresetId;

    public ThemePresetPreviewCard()
    {
        InitializeComponent();
        DataContextChanged += OnDataContextChanged;
    }

    private void OnDataContextChanged(FrameworkElement sender, DataContextChangedEventArgs args)
    {
        var preset = ResolvePreset(args.NewValue);
        ApplyPreset(preset);
    }

    private static ThemePreset? ResolvePreset(object? context) =>
        context switch
        {
            ThemePresetListItem item => item.Preset,
            ThemePreset preset => preset,
            _ => null
        };

    private void ApplyPreset(ThemePreset? preset)
    {
        _loadCts?.Cancel();
        _loadCts = new CancellationTokenSource();
        var token = _loadCts.Token;

        if (preset is null)
        {
            ClearPreview();
            _appliedPresetId = null;
            return;
        }

        // После ReplacePreset у ListItem тот же DataContext — DataContextChanged может не сработать
        _appliedPresetId = preset.Id;
        ApplyTextStyle(preset);
        SolidBackground.Background = ThemePresetPreviewHelper.CreateBrush(
            preset.Colors.Background,
            Colors.Black);

        WallpaperImage.Source = null;
        WallpaperImage.Visibility = Visibility.Collapsed;

        _ = LoadWallpaperAsync(preset, token);
    }

    public void Refresh()
    {
        ApplyPreset(ResolvePreset(DataContext));
    }

    private void ApplyTextStyle(ThemePreset preset)
    {
        TextHost.Children.Clear();

        var sample = ThemePresetPreviewTexts.John316;
        var fontFamily = new FontFamily(ThemePresetPreviewHelper.GetFontFamilyName(preset));
        var weight = ThemePresetPreviewHelper.GetFontWeight(preset);
        var align = ThemePresetPreviewHelper.GetTextAlignment(preset);
        var fill = ThemePresetPreviewHelper.CreateBrush(preset.Colors.Primary, Colors.White);

        var outlineEnabled = preset.TextOutlineEnabled && preset.TextOutlineThickness > 0.1;
        if (outlineEnabled)
        {
            var outlineBrush = ThemePresetPreviewHelper.CreateBrush(preset.TextOutlineColor, Colors.Black);
            var opacity = Math.Clamp(preset.TextOutlineOpacity, 0, 1);
            if (opacity < 0.999)
            {
                outlineBrush = new SolidColorBrush(global::Windows.UI.Color.FromArgb(
                    (byte)Math.Round(outlineBrush.Color.A * opacity),
                    outlineBrush.Color.R,
                    outlineBrush.Color.G,
                    outlineBrush.Color.B));
            }

            var step = Math.Clamp(preset.TextOutlineThickness * 0.35, 0.6, 1.4);
            foreach (var (ox, oy) in OutlineOffsets)
            {
                TextHost.Children.Add(CreateLine(
                    sample,
                    fontFamily,
                    weight,
                    align,
                    outlineBrush,
                    ox * step,
                    oy * step));
            }
        }

        TextHost.Children.Add(CreateLine(sample, fontFamily, weight, align, fill, 0, 0));
    }

    private static TextBlock CreateLine(
        string text,
        FontFamily fontFamily,
        FontWeight weight,
        TextAlignment align,
        Brush foreground,
        double offsetX,
        double offsetY)
    {
        var block = new TextBlock
        {
            Text = text,
            FontFamily = fontFamily,
            FontSize = 14,
            FontWeight = weight,
            TextAlignment = align,
            TextWrapping = TextWrapping.WrapWholeWords,
            Foreground = foreground,
            IsHitTestVisible = false
        };

        if (Math.Abs(offsetX) > 0.01 || Math.Abs(offsetY) > 0.01)
        {
            block.RenderTransform = new TranslateTransform
            {
                X = offsetX,
                Y = offsetY
            };
        }

        return block;
    }

    private async Task LoadWallpaperAsync(ThemePreset preset, CancellationToken token)
    {
        var path = ThemePresetPreviewHelper.GetPreviewWallpaperPath(preset);
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
        {
            return;
        }

        try
        {
            ImageSource? source;
            if (IsVideoPath(path))
            {
                var file = await StorageFile.GetFileFromPathAsync(path);
                using var thumb = await file.GetThumbnailAsync(
                    ThumbnailMode.SingleItem,
                    640,
                    ThumbnailOptions.ResizeThumbnail);
                if (thumb is null)
                {
                    return;
                }

                var bitmap = new BitmapImage();
                await bitmap.SetSourceAsync(thumb);
                source = bitmap;
            }
            else
            {
                var bitmap = new BitmapImage
                {
                    UriSource = new Uri(path, UriKind.Absolute)
                };
                source = bitmap;
            }

            if (token.IsCancellationRequested)
            {
                return;
            }

            WallpaperImage.Source = source;
            WallpaperImage.Visibility = Visibility.Visible;
        }
        catch
        {
            // Превью обоев необязательно — остаётся цвет фона.
        }
    }

    private void ClearPreview()
    {
        SolidBackground.Background = new SolidColorBrush(Colors.Transparent);
        WallpaperImage.Source = null;
        WallpaperImage.Visibility = Visibility.Collapsed;
        TextHost.Children.Clear();
    }

    private static bool IsVideoPath(string path)
    {
        var ext = Path.GetExtension(path);
        return !string.IsNullOrEmpty(ext) && VideoExtensions.Contains(ext);
    }
}
