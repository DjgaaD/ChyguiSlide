namespace ChyguiSlide.Services.Models;

/// <summary>Анимация при переключении секции / слайда на проекции.</summary>
public enum SectionTransitionMode
{
    /// <summary>Мгновенная смена без анимации.</summary>
    None = 0,
    /// <summary>Кроссфейд: старый и новый текст одновременно меняют прозрачность.</summary>
    CrossFade = 1,
    /// <summary>Затухание: 100→0, смена слайда, затем 0→100.</summary>
    FadeThrough = 2,
    /// <summary>Fade + Slide: новый текст проявляется с лёгким подъёмом снизу вверх.</summary>
    FadeSlide = 3,
    /// <summary>Blur → Sharp: новый текст сначала размыт, затем фокусируется вместе с fade-in.</summary>
    BlurSharp = 4,
    /// <summary>Line-by-line Stagger: каждая строка анимируется с небольшой задержкой.</summary>
    Stagger = 5
}

public static class SectionTransitionModeExtensions
{
    public static string GetTitle(this SectionTransitionMode mode) => mode switch
    {
        SectionTransitionMode.None => "Без анимации",
        SectionTransitionMode.CrossFade => "Кроссфейд",
        SectionTransitionMode.FadeThrough => "Через прозрачность",
        SectionTransitionMode.FadeSlide => "Fade + Slide",
        SectionTransitionMode.BlurSharp => "Blur → Sharp",
        SectionTransitionMode.Stagger => "Построчно",
        _ => mode.ToString()
    };

    public static string GetDescription(this SectionTransitionMode mode) => mode switch
    {
        SectionTransitionMode.None => "Текст меняется сразу.",
        SectionTransitionMode.CrossFade => "Старый и новый текст одновременно растворяются друг в друге.",
        SectionTransitionMode.FadeThrough => "Старый текст гаснет до 0%, затем появляется новый — от 0% до 100%.",
        SectionTransitionMode.FadeSlide => "Новый текст проявляется с лёгким подъёмом снизу вверх.",
        SectionTransitionMode.BlurSharp => "Новый текст сначала размыт, затем фокусируется вместе с проявлением.",
        SectionTransitionMode.Stagger => "Каждая строка анимируется с небольшой задержкой относительно предыдущей.",
        _ => string.Empty
    };

    public static bool UsesDuration(this SectionTransitionMode mode) =>
        mode is SectionTransitionMode.CrossFade 
            or SectionTransitionMode.FadeThrough 
            or SectionTransitionMode.FadeSlide 
            or SectionTransitionMode.BlurSharp 
            or SectionTransitionMode.Stagger;
}
