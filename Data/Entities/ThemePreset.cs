using System.ComponentModel.DataAnnotations;
using ChyguiSlide.Data.Enums;
using ChyguiSlide.Data.ValueObjects;
using ChyguiSlide.Services.Models;

namespace ChyguiSlide.Data.Entities;

public class ThemePreset
{
    public Guid Id { get; set; } = Guid.NewGuid();

    [MaxLength(128)]
    public required string Name { get; set; }

    [MaxLength(128)]
    public string? FontFamily { get; set; }

    public bool IsBold { get; set; } = false;

    public string TextAlignment { get; set; } = "Center";

    public SectionTransitionMode SectionTransitionMode { get; set; } = SectionTransitionMode.CrossFade;

    /// <summary>Длительность анимации смены слайда в миллисекундах.</summary>
    public int SectionTransitionDurationMs { get; set; } = 750;

    public ThemeColors Colors { get; set; } = ThemeColors.Default;

    /// <summary>Устаревшее поле: один фон. Сохраняется для совместимости / миграции в Wallpapers.</summary>
    [MaxLength(512)]
    public string? BackgroundMediaPath { get; set; }

    public bool LoopBackgroundMedia { get; set; } = true;

    /// <summary>false — общие обои; true — отдельные пулы для песен и Библии.</summary>
    public bool UseSeparateBackgrounds { get; set; }

    public ThemeBackgroundPickMode BackgroundPickMode { get; set; } = ThemeBackgroundPickMode.Fixed;

    public Guid? SelectedSharedWallpaperId { get; set; }

    public Guid? SelectedSongWallpaperId { get; set; }

    public Guid? SelectedBibleWallpaperId { get; set; }

    public bool TextOutlineEnabled { get; set; }

    public double TextOutlineThickness { get; set; } = 2;

    [MaxLength(16)]
    public string TextOutlineColor { get; set; } = "#000000";

    /// <summary>Непрозрачность контура 0…1.</summary>
    public double TextOutlineOpacity { get; set; } = 1;

    public ICollection<ThemeWallpaper> Wallpapers { get; set; } = new List<ThemeWallpaper>();

    public ICollection<Playlist> Playlists { get; set; } = new List<Playlist>();
    public ICollection<PerformanceHistory> Performances { get; set; } = new List<PerformanceHistory>();
}

