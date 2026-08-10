namespace ChyguiSlide.Services.Models;

public sealed class BibleVerse
{
    public required string BookId { get; init; }
    public required string BookName { get; init; }
    public int Chapter { get; init; }
    public int Verse { get; init; }
    public required string Text { get; init; }

    public string Reference => $"{Chapter}:{Verse}";
}

public sealed class BibleBook
{
    public required string BookId { get; init; }
    public required string EnglishName { get; init; }
    public required string RussianName { get; init; }
    public required string Abbreviation { get; init; }
    public bool IsNewTestament { get; init; }
    public int Order { get; init; }
    public int ChapterCount { get; init; }

    public string DisplayName => RussianName;
}

public enum BibleTestamentFilter
{
    All,
    OldTestament,
    NewTestament
}
