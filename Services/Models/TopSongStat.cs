namespace ChyguiSlide.Services.Models;

public sealed class TopSongStat
{
    public Guid SongId { get; init; }
    public string Title { get; init; } = string.Empty;
    public int? Number { get; init; }
    public string? CollectionName { get; init; }
    public Guid? CollectionId { get; init; }
    public int PlayCount { get; init; }
    public DateTime? LastPlayedAt { get; init; }
    public int Rank { get; init; }
}
