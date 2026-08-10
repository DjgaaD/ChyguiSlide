using System.ComponentModel.DataAnnotations;

namespace ChyguiSlide.Data.Entities;

public class Tag
{
    public Guid Id { get; set; } = Guid.NewGuid();

    [MaxLength(128)]
    public required string Name { get; set; }

    [MaxLength(16)]
    public string? Color { get; set; }

    public ICollection<SongTag> SongTags { get; set; } = new List<SongTag>();
}

