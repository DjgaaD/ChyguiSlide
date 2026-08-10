using System.ComponentModel.DataAnnotations;

namespace ChyguiSlide.Data.Entities;

/// <summary>Сборник песен (например «Песнь возрождения», «Хоровые песни»).</summary>
public class SongCollection
{
    public Guid Id { get; set; } = Guid.NewGuid();

    [MaxLength(256)]
    public required string Name { get; set; }

    [MaxLength(512)]
    public string? Description { get; set; }

    public int SortOrder { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

    public ICollection<Song> Songs { get; set; } = new List<Song>();
}
