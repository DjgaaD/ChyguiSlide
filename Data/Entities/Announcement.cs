using System.ComponentModel.DataAnnotations;

namespace ChyguiSlide.Data.Entities;

/// <summary>
/// Объявление для показа на экране (быстрое сохраняется опционально, постоянное — в каталоге).
/// </summary>
public class Announcement
{
    public Guid Id { get; set; } = Guid.NewGuid();

    [MaxLength(256)]
    public string Title { get; set; } = string.Empty;

    /// <summary>Текст. Пустая строка между абзацами = новый слайд.</summary>
    public required string Content { get; set; }

    /// <summary>Постоянное (в списке) vs разовое черновик не храним здесь.</summary>
    public bool IsPermanent { get; set; } = true;

    /// <summary>Закреплено сверху списка.</summary>
    public bool IsPinned { get; set; }

    public int SortOrder { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}
