using ChyguiSlide.Data.Entities;
using ChyguiSlide.Data.Enums;
using Microsoft.UI;
using Microsoft.UI.Text;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;
using Windows.UI;
using Windows.UI.Text;

namespace ChyguiSlide.Services;

public static class ThemePresetPreviewHelper
{
    /// <summary>
    /// Путь к обоям для превью (стих Библии → пул Bible).
    /// Для Random показываем первый файл пула — стабильное превью.
    /// </summary>
    public static string? GetPreviewWallpaperPath(ThemePreset preset)
    {
        if (preset.BackgroundPickMode == ThemeBackgroundPickMode.SolidColor)
        {
            return null;
        }

        var pool = ThemeBackgroundResolver.ResolvePool(preset, isBibleContent: true);
        var items = ThemeBackgroundResolver.GetPoolItems(preset, pool);
        if (items.Count == 0)
        {
            return string.IsNullOrWhiteSpace(preset.BackgroundMediaPath)
                ? null
                : preset.BackgroundMediaPath.Trim();
        }

        if (preset.BackgroundPickMode == ThemeBackgroundPickMode.Fixed)
        {
            var selectedId = ThemeBackgroundResolver.GetSelectedId(preset, pool);
            var chosen = items.FirstOrDefault(w => w.Id == selectedId) ?? items[0];
            return string.IsNullOrWhiteSpace(chosen.FilePath) ? null : chosen.FilePath.Trim();
        }

        var first = items[0];
        return string.IsNullOrWhiteSpace(first.FilePath) ? null : first.FilePath.Trim();
    }

    public static SolidColorBrush CreateBrush(string? hex, Color fallback)
    {
        if (TryParseHexColor(hex, out var color))
        {
            return new SolidColorBrush(color);
        }

        return new SolidColorBrush(fallback);
    }

    public static double GetEffectiveOutlineThickness(ThemePreset preset) =>
        preset.TextOutlineEnabled ? preset.TextOutlineThickness : 0;

    public static FontWeight GetFontWeight(ThemePreset preset) =>
        preset.IsBold ? FontWeights.Bold : FontWeights.Normal;

    public static TextAlignment GetTextAlignment(ThemePreset preset) =>
        preset.TextAlignment switch
        {
            "Left" => TextAlignment.Left,
            "Right" => TextAlignment.Right,
            "Justify" => TextAlignment.Justify,
            _ => TextAlignment.Center
        };

    public static string GetFontFamilyName(ThemePreset preset) =>
        string.IsNullOrWhiteSpace(preset.FontFamily) ? "Segoe UI" : preset.FontFamily.Trim();

    public static bool TryParseHexColor(string? hex, out Color color)
    {
        color = Colors.Transparent;

        if (string.IsNullOrWhiteSpace(hex))
        {
            return false;
        }

        var cleaned = hex.TrimStart('#');
        if (cleaned.Length is not (6 or 8))
        {
            return false;
        }

        if (!uint.TryParse(cleaned, System.Globalization.NumberStyles.HexNumber, null, out var value))
        {
            return false;
        }

        if (cleaned.Length == 6)
        {
            color = Color.FromArgb(
                255,
                (byte)((value >> 16) & 0xFF),
                (byte)((value >> 8) & 0xFF),
                (byte)(value & 0xFF));
            return true;
        }

        color = Color.FromArgb(
            (byte)((value >> 24) & 0xFF),
            (byte)((value >> 16) & 0xFF),
            (byte)((value >> 8) & 0xFF),
            (byte)(value & 0xFF));
        return true;
    }
}
