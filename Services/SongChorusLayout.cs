using ChyguiSlide.Data.Entities;
using ChyguiSlide.Data.Enums;
using ChyguiSlide.Data.ValueObjects;

namespace ChyguiSlide.Services;

/// <summary>
/// Раскладка куплетов/припева: после каждого куплета — припев (если его ещё нет сразу после).
/// </summary>
public static class SongChorusLayout
{
    /// <returns>Сколько копий припева вставлено.</returns>
    public static int ExpandChorusAfterEachVerse(IList<SongSection> sections)
    {
        ArgumentNullException.ThrowIfNull(sections);

        var chorus = sections.FirstOrDefault(s => s.SectionType == SectionType.Chorus);
        if (chorus is null || sections.All(s => s.SectionType != SectionType.Verse))
        {
            return 0;
        }

        var inserted = 0;
        var index = 0;
        while (index < sections.Count)
        {
            if (sections[index].SectionType != SectionType.Verse)
            {
                index++;
                continue;
            }

            var nextIndex = index + 1;
            var nextIsChorus = nextIndex < sections.Count &&
                               sections[nextIndex].SectionType == SectionType.Chorus;

            if (!nextIsChorus)
            {
                sections.Insert(nextIndex, CloneChorus(chorus));
                inserted++;
            }

            index = nextIndex + 1;
        }

        for (var i = 0; i < sections.Count; i++)
        {
            sections[i].Order = i;
        }

        return inserted;
    }

    private static SongSection CloneChorus(SongSection chorus) => new()
    {
        Id = Guid.NewGuid(),
        SongId = chorus.SongId,
        SectionType = SectionType.Chorus,
        Heading = chorus.Heading,
        Content = chorus.Content,
        Notes = chorus.Notes,
        Timing = new SectionTiming(
            chorus.Timing?.DurationSeconds,
            chorus.Timing?.Bpm,
            chorus.Timing?.StartMeasure)
    };
}
