using System.ComponentModel.DataAnnotations;
using ChyguiSlide.Data.Enums;

namespace ChyguiSlide.Data.Entities;

public class ImportJob
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public ImportSourceType SourceType { get; set; } = ImportSourceType.LocalFile;

    [MaxLength(1024)]
    public required string SourceLocation { get; set; }

    public ImportJobStatus Status { get; set; } = ImportJobStatus.Pending;

    [Range(0, 1)]
    public double Progress { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? CompletedAt { get; set; }

    public string? ErrorMessage { get; set; }

    public ICollection<Attachment> Attachments { get; set; } = new List<Attachment>();
}

