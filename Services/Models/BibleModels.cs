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

    /// <summary>Цвет плитки в сетке выбора (по разделу канона).</summary>
    public string CategoryColorHex => Order switch
    {
        <= 5 => "#8B6B3D",   // Пятикнижие
        <= 17 => "#B08948",  // Исторические
        <= 22 => "#A04A4A",  // Поэтические
        <= 39 => "#6B5B95",  // Пророки
        <= 43 => "#3D6FA8",  // Евангелия
        44 => "#2E8B8B",     // Деяния
        <= 57 => "#4A8F5C",  // Послания Павла
        <= 65 => "#6B8F3A",  // Соборные
        _ => "#9A9A32"       // Откровение
    };
}

public enum BibleTestamentFilter
{
    All,
    OldTestament,
    NewTestament
}
