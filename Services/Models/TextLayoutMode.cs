namespace ChyguiSlide.Services.Models;

/// <summary>
/// Режим размещения текста на слайде проекции.
/// </summary>
public enum TextLayoutMode
{
    /// <summary>
    /// Автоматический подбор самого крупного шрифта, при котором слайд целиком помещается на экран.
    /// </summary>
    AutoMaxFit = 0,

    /// <summary>
    /// Без переноса: каждая исходная строка остаётся одной, шрифт уменьшается под ширину.
    /// </summary>
    ShrinkToFit = 1
}

public static class TextLayoutModeExtensions
{
    public static string GetTitle(this TextLayoutMode mode) => mode switch
    {
        TextLayoutMode.ShrinkToFit => "Без переноса",
        TextLayoutMode.AutoMaxFit => "Авто",
        _ => mode.ToString()
    };

    public static string GetDescription(this TextLayoutMode mode) => mode switch
    {
        TextLayoutMode.ShrinkToFit => "Исходные строки не переносятся — при необходимости уменьшается весь слайд.",
        TextLayoutMode.AutoMaxFit => "Подбирает наибольший шрифт, при котором весь слайд помещается на экран.",
        _ => string.Empty
    };
}
