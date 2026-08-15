namespace ChyguiSlide.Services.Models;

public record ProjectionState(
    Guid? SongId,
    Guid? PlaylistId,
    string? SongTitle,
    int SectionIndex,
    IReadOnlyList<string> VisibleLines,
    DateTimeOffset UpdatedAt,
    string? ReferenceCaption)
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

