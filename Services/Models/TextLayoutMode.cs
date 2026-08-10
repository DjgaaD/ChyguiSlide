namespace ChyguiSlide.Services.Models;

/// <summary>
/// Режим размещения текста на слайде проекции.
/// </summary>
public enum TextLayoutMode
{
    /// <summary>
    /// Текущий алгоритм: разбиение строк для максимально крупного шрифта.
    /// </summary>
    MaximizeFont = 0,

    /// <summary>
    /// Перенос длинных строк по ширине экрана (крупный шрифт, как Holyrics «с переносом»).
    /// </summary>
    WrapToWidth = 1,

    /// <summary>
    /// Без переноса: каждая исходная строка остаётся одной, шрифт уменьшается под ширину.
    /// </summary>
    ShrinkToFit = 2,

    /// <summary>
    /// Binary search по размеру шрифта: максимальный шрифт, при котором слайд целиком
    /// помещается на экран; перенос только внутри исходных строк.
    /// </summary>
    AutoMaxFit = 3
}

public static class TextLayoutModeExtensions
{
    public static string GetTitle(this TextLayoutMode mode) => mode switch
    {
        TextLayoutMode.MaximizeFont => "Максимальный размер",
        TextLayoutMode.WrapToWidth => "Перенос по ширине",
        TextLayoutMode.ShrinkToFit => "Без переноса",
        TextLayoutMode.AutoMaxFit => "Авто: макс. шрифт",
        _ => mode.ToString()
    };

    public static string GetDescription(this TextLayoutMode mode) => mode switch
    {
        TextLayoutMode.MaximizeFont => "Текущий режим: строки перестраиваются так, чтобы шрифт был как можно крупнее.",
        TextLayoutMode.WrapToWidth => "Длинные строки переносятся по ширине экрана, шрифт остаётся крупным.",
        TextLayoutMode.ShrinkToFit => "Исходные строки не переносятся — при необходимости уменьшается весь слайд.",
        TextLayoutMode.AutoMaxFit => "Подбирает наибольший шрифт, при котором весь слайд помещается на экран.",
        _ => string.Empty
    };
}
