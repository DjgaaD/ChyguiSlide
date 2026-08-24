using System.Numerics;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Hosting;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Animation;
using Windows.Foundation;

namespace ChyguiSlide.Views.UiAnimation;

/// <summary>
/// Примитивы движения полосы выделения. Горизонталь — Storyboard, вертикаль — Composition.
/// </summary>
internal static class SelectionStripeMotion
{
    public static void MoveHorizontal(
        FrameworkElement stripe,
        TranslateTransform transform,
        FrameworkElement? target,
        UIElement relativeTo,
        bool animate)
    {
        if (target is null || target.ActualWidth < 1 || target.ActualHeight < 1)
        {
            stripe.Opacity = 0;
            return;
        }

        Point origin;
        try
        {
            origin = target.TransformToVisual(relativeTo).TransformPoint(new Point(0, 0));
        }
        catch
        {
            stripe.Opacity = 0;
            return;
        }

        stripe.Opacity = 1;
        var duration = animate ? TimeSpan.FromMilliseconds(280) : TimeSpan.Zero;
        AnimateDouble(stripe, "Width", Math.Max(8, target.ActualWidth), duration);
        AnimateDouble(transform, "X", origin.X, duration);
        transform.Y = 0;
    }

    public static void MoveVertical(
        FrameworkElement stripe,
        FrameworkElement? target,
        UIElement relativeTo,
        bool animate)
    {
        if (target is null || target.ActualWidth < 1 || target.ActualHeight < 1)
        {
            stripe.Opacity = 0;
            return;
        }

        Point origin;
        try
        {
            origin = target.TransformToVisual(relativeTo).TransformPoint(new Point(0, 0));
        }
        catch
        {
            stripe.Opacity = 0;
            return;
        }

        var hostHeight = relativeTo is FrameworkElement host && host.ActualHeight > 0
            ? host.ActualHeight
            : double.PositiveInfinity;
        var itemHeight = Math.Max(8, target.ActualHeight);
        var top = origin.Y;
        var bottom = top + itemHeight;
        if (bottom <= 0.5 || top >= hostHeight - 0.5)
        {
            stripe.Opacity = 0;
            return;
        }

        var clippedTop = Math.Max(0, top);
        var clippedBottom = Math.Min(hostHeight, bottom);
        var clippedHeight = Math.Max(0, clippedBottom - clippedTop);
        if (clippedHeight < 2)
        {
            stripe.Opacity = 0;
            return;
        }

        stripe.Opacity = 1;
        stripe.Width = 3;

        var visual = ElementCompositionPreview.GetElementVisual(stripe);
        var compositor = visual.Compositor;
        var toOffset = new Vector3(0, (float)clippedTop, 0);
        var toSize = new Vector2(3, (float)clippedHeight);
        var fromOffset = visual.Offset;
        var fromSize = visual.Size.Y >= 1 ? visual.Size : new Vector2(3, (float)Math.Max(8, stripe.ActualHeight));

        visual.StopAnimation("Offset");
        visual.StopAnimation("Size");

        if (!animate)
        {
            stripe.Height = toSize.Y;
            visual.Offset = toOffset;
            visual.Size = toSize;
            return;
        }

        var offsetAnim = compositor.CreateVector3KeyFrameAnimation();
        offsetAnim.Duration = TimeSpan.FromMilliseconds(280);
        offsetAnim.InsertKeyFrame(0f, fromOffset);
        offsetAnim.InsertKeyFrame(
            1f,
            toOffset,
            compositor.CreateCubicBezierEasingFunction(new Vector2(0.16f, 1f), new Vector2(0.3f, 1f)));

        var sizeAnim = compositor.CreateVector2KeyFrameAnimation();
        sizeAnim.Duration = offsetAnim.Duration;
        sizeAnim.InsertKeyFrame(0f, fromSize);
        sizeAnim.InsertKeyFrame(
            1f,
            toSize,
            compositor.CreateCubicBezierEasingFunction(new Vector2(0.16f, 1f), new Vector2(0.3f, 1f)));

        var batch = compositor.CreateScopedBatch(Microsoft.UI.Composition.CompositionBatchTypes.Animation);
        batch.Completed += (_, _) =>
        {
            stripe.DispatcherQueue.TryEnqueue(() =>
            {
                stripe.Height = toSize.Y;
                visual.Offset = toOffset;
                visual.Size = toSize;
            });
        };
        visual.StartAnimation("Offset", offsetAnim);
        visual.StartAnimation("Size", sizeAnim);
        batch.End();
    }

    private static void AnimateDouble(DependencyObject target, string property, double to, TimeSpan duration)
    {
        var animation = new DoubleAnimation
        {
            To = to,
            Duration = duration,
            EasingFunction = duration > TimeSpan.Zero
                ? new CubicEase { EasingMode = EasingMode.EaseOut }
                : null,
            EnableDependentAnimation = true
        };

        Storyboard.SetTarget(animation, target);
        Storyboard.SetTargetProperty(animation, property);
        var storyboard = new Storyboard();
        storyboard.Children.Add(animation);
        storyboard.Begin();
    }
}
