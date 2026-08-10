namespace ChyguiSlide.Data.Entities;

public class SongTag
{
    public Guid SongId { get; set; }
    public Song Song { get; set; } = null!;

    public Guid TagId { get; set; }
    public Tag Tag { get; set; } = null!;
}

