using System;
using System.Threading.Tasks;
using ChyguiSlide.Services.Models;
using Microsoft.UI.Composition;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Hosting;

namespace ChyguiSlide.Windows;

/// <summary>
/// Плавный кроссфейд: старый и новый текст одновременно меняют прозрачность (Composition).
/// </summary>
public sealed class ProjectionTransitionPlayer
{
    private readonly UIElement _outgoing;
    private readonly UIElement _incoming;
    private bool _busy;

    public ProjectionTransitionPlayer(UIElement outgoing, UIElement incoming)
    {
        _outgoing = outgoing ?? throw new ArgumentNullException(nameof(outgoing));
        _incoming = incoming ?? throw new ArgumentNullException(nameof(incoming));
        SetOpacity(_outgoing, 0);
        SetOpacity(_incoming, 1);
    }

    public async Task PlayAsync(SectionTransitionMode mode, Func<Task> applyContentAsync)
    {
        ArgumentNullException.ThrowIfNull(applyContentAsync);

        if (mode == SectionTransitionMode.None || _busy)
        {
            SetOpacity(_outgoing, 0);
            SetOpacity(_incoming, 1);
            await applyContentAsync().ConfigureAwait(true);
            return;
        }

        _busy = true;
        try
        {
            // Старый слой уже заполнен VM и виден; новый пока прячем
            SetOpacity(_outgoing, 1);
            SetOpacity(_incoming, 0);

            await applyContentAsync().ConfigureAwait(true);

            // Дать UI применить новые строки под Opacity=0
            await Task.Delay(48).ConfigureAwait(true);

            await CrossFadeAsync(750).ConfigureAwait(true);

            SetOpacity(_outgoing, 0);
            SetOpacity(_incoming, 1);
        }
        finally
        {
            _busy = false;
        }
    }

    private Task CrossFadeAsync(int durationMs)
    {
        var outVisual = ElementCompositionPreview.GetElementVisual(_outgoing);
        var inVisual = ElementCompositionPreview.GetElementVisual(_incoming);
        var compositor = outVisual.Compositor;

        var tcs = new TaskCompletionSource();
        var batch = compositor.CreateScopedBatch(CompositionBatchTypes.Animation);
        batch.Completed += (_, _) => tcs.TrySetResult();

        var duration = TimeSpan.FromMilliseconds(durationMs);

        var fadeOut = compositor.CreateScalarKeyFrameAnimation();
        fadeOut.InsertKeyFrame(0f, 1f);
        fadeOut.InsertKeyFrame(1f, 0f, compositor.CreateCubicBezierEasingFunction(
            new System.Numerics.Vector2(0.4f, 0f),
            new System.Numerics.Vector2(0.2f, 1f)));
        fadeOut.Duration = duration;

        var fadeIn = compositor.CreateScalarKeyFrameAnimation();
        fadeIn.InsertKeyFrame(0f, 0f);
        fadeIn.InsertKeyFrame(1f, 1f, compositor.CreateCubicBezierEasingFunction(
            new System.Numerics.Vector2(0.4f, 0f),
            new System.Numerics.Vector2(0.2f, 1f)));
        fadeIn.Duration = duration;

        outVisual.StartAnimation("Opacity", fadeOut);
        inVisual.StartAnimation("Opacity", fadeIn);
        batch.End();

        return tcs.Task;
    }

    private static void SetOpacity(UIElement element, float opacity)
    {
        ElementCompositionPreview.GetElementVisual(element).Opacity = opacity;
    }
}
