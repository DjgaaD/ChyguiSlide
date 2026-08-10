using System.ComponentModel.DataAnnotations;
using ChyguiSlide.Data.Enums;
using ChyguiSlide.Data.ValueObjects;

namespace ChyguiSlide.Data.Entities;

public class Attachment
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public Guid SongId { get; set; }
    public Song Song { get; set; } = null!;

    public Guid? ImportJobId { get; set; }
    public ImportJob? ImportJob { get; set; }

    public AttachmentKind Kind { get; set; } = AttachmentKind.Unknown;

    [MaxLength(512)]
    public required string FilePath { get; set; }

    [MaxLength(1024)]
    public string? CloudUri { get; set; }

    public CloudLocation CloudLocation { get; set; } = CloudLocation.Empty;

    [MaxLength(128)]
    public string? MimeType { get; set; }

    [MaxLength(64)]
    public string? Version { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}

