namespace ChyguiSlide.Services.Models;

public sealed class PresentationImportResult
{
    public string? Title { get; init; }
    public IReadOnlyList<PresentationSlide> Slides { get; init; } = Array.Empty<PresentationSlide>();
}

public sealed class PresentationSlide
{
    public required string Heading { get; init; }
    public required string Content { get; init; }
}












