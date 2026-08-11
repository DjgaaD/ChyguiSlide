namespace ChyguiSlide.Services.Models;

/// <summary>Анимация при переключении секции / слайда на проекции.</summary>
public enum SectionTransitionMode
{
    /// <summary>Мгновенная смена без анимации.</summary>
    None = 0,
    /// <summary>Кроссфейд: старый и новый текст одновременно меняют прозрачность.</summary>
    CrossFade = 1,
    /// <summary>Затухание: 100→0, смена слайда, затем 0→100.</summary>
    FadeThrough = 2
}

public static class SectionTransitionModeExtensions
{
    public static string GetTitle(this SectionTransitionMode mode) => mode switch
    {
        SectionTransitionMode.None => "Без анимации",
        SectionTransitionMode.CrossFade => "Кроссфейд",
        SectionTransitionMode.FadeThrough => "Через прозрачность",
        _ => mode.ToString()
    };

    public static string GetDescription(this SectionTransitionMode mode) => mode switch
    {
        SectionTransitionMode.None => "Текст меняется сразу.",
        SectionTransitionMode.CrossFade => "Старый и новый текст одновременно растворяются друг в друге.",
        SectionTransitionMode.FadeThrough => "Старый текст гаснет до 0%, затем появляется новый — от 0% до 100%.",
        _ => string.Empty
    };

    public static bool UsesDuration(this SectionTransitionMode mode) =>
        mode is SectionTransitionMode.CrossFade or SectionTransitionMode.FadeThrough;
}
