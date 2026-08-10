using System.ComponentModel.DataAnnotations;

namespace ChyguiSlide.Data.Entities;

public class PerformanceHistory
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public Guid SongId { get; set; }
    public Song Song { get; set; } = null!;

    public Guid? PlaylistId { get; set; }
    public Playlist? Playlist { get; set; }

    public Guid? ThemePresetId { get; set; }
    public ThemePreset? ThemePreset { get; set; }

    public DateTime PlayedAt { get; set; } = DateTime.UtcNow;

    [MaxLength(128)]
    public string? OperatorName { get; set; }

    public string? Notes { get; set; }
}

