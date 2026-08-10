using System.ComponentModel.DataAnnotations;
using ChyguiSlide.Data.Enums;

namespace ChyguiSlide.Data.Entities;

public class ThemeWallpaper
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public Guid ThemePresetId { get; set; }

    public ThemePreset? ThemePreset { get; set; }

    [MaxLength(512)]
    public string FilePath { get; set; } = string.Empty;

    [MaxLength(256)]
    public string DisplayName { get; set; } = string.Empty;

    public ThemeWallpaperPool Pool { get; set; } = ThemeWallpaperPool.Shared;

    public int SortOrder { get; set; }
}
