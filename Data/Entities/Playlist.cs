using System.ComponentModel.DataAnnotations;

namespace ChyguiSlide.Data.Entities;

public class Playlist
{
    public Guid Id { get; set; } = Guid.NewGuid();

    [MaxLength(256)]
    public required string Name { get; set; }

    [MaxLength(128)]
    public string? EventType { get; set; }

    public DateTime? ScheduledAt { get; set; }

    [MaxLength(256)]
    public string? Location { get; set; }

    public Guid? ThemePresetId { get; set; }
    public ThemePreset? ThemePreset { get; set; }

    public ICollection<PlaylistEntry> Entries { get; set; } = new List<PlaylistEntry>();
    public ICollection<PerformanceHistory> Performances { get; set; } = new List<PerformanceHistory>();
}

