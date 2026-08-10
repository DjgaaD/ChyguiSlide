namespace ChyguiSlide.Services.Models;

/// <summary>Где показывать ссылку на стих.</summary>
public enum BibleReferencePlacement
{
    /// <summary>Над блоком текста стиха.</summary>
    Above = 0,
    /// <summary>Под блоком текста стиха.</summary>
    Below = 1,
    /// <summary>Сразу после последнего слова текста (часть раскладки/переноса).</summary>
    After = 2,
    /// <summary>Фиксированно у верхнего края экрана, без участия в масштабировании стиха.</summary>
    TopOfScreen = 3,
    /// <summary>Фиксированно у нижнего края экрана, без участия в масштабировании стиха.</summary>
    BottomOfScreen = 4
}

public static class BibleReferencePlacementExtensions
{
    public static string GetTitle(this BibleReferencePlacement placement) => placement switch
    {
        BibleReferencePlacement.Above => "Над текстом",
        BibleReferencePlacement.Below => "Под текстом",
        BibleReferencePlacement.After => "После текста",
        BibleReferencePlacement.TopOfScreen => "Сверху экрана",
        BibleReferencePlacement.BottomOfScreen => "Снизу экрана",
        _ => placement.ToString()
    };

    public static bool IsRelativeToText(this BibleReferencePlacement placement) =>
        placement is BibleReferencePlacement.Above
            or BibleReferencePlacement.Below
            or BibleReferencePlacement.After;

    public static bool IsPinnedToScreenEdge(this BibleReferencePlacement placement) =>
        placement is BibleReferencePlacement.TopOfScreen
            or BibleReferencePlacement.BottomOfScreen;
}
