namespace ChyguiSlide.Services.Models;

public record class ProjectionState(
    Guid? SongId,
    Guid? PlaylistId,
    string? SongTitle,
    int SectionIndex,
    IReadOnlyList<string> VisibleLines,
    DateTimeOffset UpdatedAt,
    string? ReferenceCaption = null)
{
    public static ProjectionState Empty { get; } = new(
        null,
        null,
        null,
        0,
        Array.Empty<string>(),
        DateTimeOffset.MinValue,
        null);
}

