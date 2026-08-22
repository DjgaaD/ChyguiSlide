namespace ChyguiSlide.Services.Models;

    /// <summary>Стиль анимации переключения слайдов на проекции (Composition API).</summary>
public enum TransitionStyle
{
    /// <summary>Crossfade: два слоя одновременно растворяются друг в друге.</summary>
    Fade = 0,
    /// <summary>Fade + вертикальное движение (два слоя).</summary>
    FadeSlide = 1,
    /// <summary>Fade + blur (два слоя, требует GPU).</summary>
    BlurSharp = 2,
    /// <summary>Line-by-line Stagger (два слоя, построчная анимация).</summary>
    Stagger = 3
}

public static class TransitionStyleExtensions
{
    public static string GetTitle(this TransitionStyle style) => style switch
    {
        TransitionStyle.Fade => "Crossfade",
        TransitionStyle.FadeSlide => "Fade + Slide",
        TransitionStyle.BlurSharp => "Blur → Sharp",
        TransitionStyle.Stagger => "Line-by-line Stagger",
        _ => style.ToString()
    };

    public static string GetDescription(this TransitionStyle style) => style switch
    {
        TransitionStyle.Fade => "Старый и новый текст одновременно растворяются друг в друге (два слоя).",
        TransitionStyle.FadeSlide => "Новый текст проявляется с лёгким подъёмом снизу вверх. Баланс между производительностью и визуальным эффектом.",
        TransitionStyle.BlurSharp => "Новый текст сначала размыт, затем фокусируется вместе с проявлением. Требует хорошей видеокарты.",
        TransitionStyle.Stagger => "Crossfade + Slide, но каждая строка анимируется с небольшой задержкой относительно предыдущей.",
        _ => string.Empty
    };

    public static bool UsesDuration(this TransitionStyle style) => true;

    public static bool UsesTwoLayers(this TransitionStyle style) =>
        style is TransitionStyle.Fade or TransitionStyle.FadeSlide or TransitionStyle.BlurSharp or TransitionStyle.Stagger;

    public static bool UsesBlur(this TransitionStyle style) =>
        style == TransitionStyle.BlurSharp;
}
