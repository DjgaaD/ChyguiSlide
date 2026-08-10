namespace ChyguiSlide.Data.ValueObjects;

public record class SectionTiming(double? DurationSeconds, int? Bpm, int? StartMeasure)
{
    /// <summary>
    /// Каждый раз новый экземпляр: EF Core OwnsOne не допускает общий объект Timing у разных секций
    /// (ключ SongSectionId нельзя «перепривязать»).
    /// </summary>
    public static SectionTiming Empty => new(null, null, null);

    public TimeSpan? Duration =>
        DurationSeconds.HasValue ? TimeSpan.FromSeconds(DurationSeconds.Value) : null;
}

