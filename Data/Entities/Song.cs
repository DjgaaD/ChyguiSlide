using System.ComponentModel.DataAnnotations;

namespace ChyguiSlide.Data.Entities;

public class Song
{
    public Guid Id { get; set; } = Guid.NewGuid();

    [MaxLength(256)]
    public required string Title { get; set; }

    [MaxLength(256)]
    public string? Subtitle { get; set; }

    public int? Number { get; set; }

    /// <summary>Сборник (Песнь возрождения, Хоровые и т.п.).</summary>
    public Guid? CollectionId { get; set; }
    public SongCollection? Collection { get; set; }

    [MaxLength(32)]
    public string? Language { get; set; }

    public int? Tempo { get; set; }

    [MaxLength(64)]
    public string? DefaultKey { get; set; }

    public bool IsFavorite { get; set; }
    public bool IsArchived { get; set; }
    public bool IsPublished { get; set; } = true;

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

    public ICollection<SongSection> Sections { get; set; } = new List<SongSection>();
    public ICollection<SongTag> SongTags { get; set; } = new List<SongTag>();
    public ICollection<PerformanceHistory> Performances { get; set; } = new List<PerformanceHistory>();
    public ICollection<Attachment> Attachments { get; set; } = new List<Attachment>();
}

