using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;

namespace ChyguiSlide.Views;

/// <summary>Фасад анимации полосы. Реализация — <see cref="UiAnimation.SelectionStripeMotion"/>.</summary>
internal static class SelectionStripeHelper
{
    public static void MoveHorizontal(
        FrameworkElement stripe,
        TranslateTransform transform,
        FrameworkElement? target,
        UIElement relativeTo,
        bool animate)
        => UiAnimation.SelectionStripeMotion.MoveHorizontal(stripe, transform, target, relativeTo, animate);

    public static void MoveVertical(
        FrameworkElement stripe,
        FrameworkElement? target,
        UIElement relativeTo,
        bool animate)
        => UiAnimation.SelectionStripeMotion.MoveVertical(stripe, target, relativeTo, animate);
}
