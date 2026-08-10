using System.ComponentModel.DataAnnotations;
using ChyguiSlide.Data.ValueObjects;
using ChyguiSlide.Data.Enums;

namespace ChyguiSlide.Data.Entities;

public class SongSection
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public Guid SongId { get; set; }
    public Song Song { get; set; } = null!;

    public SectionType SectionType { get; set; } = SectionType.Verse;

    [Range(0, int.MaxValue)]
    public int Order { get; set; }

    [MaxLength(128)]
    public string? Heading { get; set; }

    public required string Content { get; set; }

    public string? Notes { get; set; }

    public SectionTiming Timing { get; set; } = new(null, null, null);
}

