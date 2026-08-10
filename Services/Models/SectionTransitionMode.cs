namespace ChyguiSlide.Services.Models;

/// <summary>Анимация при переключении секции / слайда на проекции.</summary>
public enum SectionTransitionMode
{
    /// <summary>Мгновенная смена без анимации.</summary>
    None = 0,
    /// <summary>Плавный кроссфейд: старый и новый текст сменяются одновременно.</summary>
    CrossFade = 1
}

public static class SectionTransitionModeExtensions
{
    public static string GetTitle(this SectionTransitionMode mode) => mode switch
    {
        SectionTransitionMode.None => "Без анимации",
        SectionTransitionMode.CrossFade => "Плавная смена",
        _ => mode.ToString()
    };

    public static string GetDescription(this SectionTransitionMode mode) => mode switch
    {
        SectionTransitionMode.None => "Текст меняется сразу.",
        SectionTransitionMode.CrossFade => "Старый текст плавно растворяется в новый, без пустого экрана.",
        _ => string.Empty
    };
}
