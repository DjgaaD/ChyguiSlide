namespace ChyguiSlide.Services.Models;

/// <summary>Вид выбора книги / главы / стиха на странице Библии.</summary>
public enum BiblePickerLayoutMode
{
    /// <summary>Списки слева направо (как сейчас).</summary>
    Lists = 0,

    /// <summary>Цветные плитки книг, глав и номеров стихов.</summary>
    Grid = 1
}
