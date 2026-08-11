using System;
using System.Threading.Tasks;
using ChyguiSlide.Services.Models;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Hosting;
using Microsoft.UI.Xaml.Media.Animation;

namespace ChyguiSlide.Windows;

/// <summary>
/// Анимации смены слайда:
/// FadeThrough — 100→0, смена, 0→100;
/// CrossFade — одновременный dissolve двух слоёв.
/// </summary>
public sealed class ProjectionTransitionPlayer
{
    private readonly UIElement _incomingLayer;
    private readonly UIElement _outgoingLayer;
    private bool _busy;
    private Func<Task>? _queuedApply;
    private int _queuedDurationMs = 750;
    private SectionTransitionMode _queuedMode = SectionTransitionMode.FadeThrough;
    private Storyboard? _activeStoryboard;

    public ProjectionTransitionPlayer(
        UIElement incomingLayer,
        UIElement outgoingLayer,
        UIElement? unusedSnapshot = null)
    {
        _incomingLayer = incomingLayer ?? throw new ArgumentNullException(nameof(incomingLayer));
        _outgoingLayer = outgoingLayer ?? throw new ArgumentNullException(nameof(outgoingLayer));
        _ = unusedSnapshot;
        ResetVisualState();
    }

    public async Task PlayAsync(SectionTransitionMode mode, Func<Task> applyContentAsync, int durationMs = 750)
    {
        ArgumentNullException.ThrowIfNull(applyContentAsync);

        if (mode == SectionTransitionMode.None)
        {
            _queuedApply = null;
            StopActiveStoryboard();
            ResetVisualState();
            await applyContentAsync().ConfigureAwait(true);
            return;
        }

        if (_busy)
        {
            _queuedApply = applyContentAsync;
            _queuedDurationMs = durationMs;
            _queuedMode = mode;
            return;
        }

        _busy = true;
        try
        {
            await RunAsync(mode, applyContentAsync, durationMs).ConfigureAwait(true);

            while (_queuedApply is not null)
            {
                var next = _queuedApply;
                var nextMs = _queuedDurationMs;
                var nextMode = _queuedMode;
                _queuedApply = null;
                await RunAsync(nextMode, next, nextMs).ConfigureAwait(true);
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[ProjectionTransition] {ex.Message}");
            StopActiveStoryboard();
            ResetVisualState();
            try
            {
                await applyContentAsync().ConfigureAwait(true);
            }
            catch
            {
                // ignore
            }
        }
        finally
        {
            _busy = false;
        }
    }

    private Task RunAsync(SectionTransitionMode mode, Func<Task> applyContentAsync, int durationMs) =>
        mode == SectionTransitionMode.CrossFade
            ? RunCrossFadeAsync(applyContentAsync, durationMs)
            : RunFadeThroughAsync(applyContentAsync, durationMs);

    /// <summary>100% → 0%, смена слайда, 0% → 100%.</summary>
    private async Task RunFadeThroughAsync(Func<Task> applyContentAsync, int durationMs)
    {
        ResetCompositionOpacity(_incomingLayer);
        ResetCompositionOpacity(_outgoingLayer);
        _outgoingLayer.Opacity = 0;
        _incomingLayer.Opacity = 1;

        var ms = Math.Clamp(durationMs <= 0 ? 750 : durationMs, 150, 3000);
        var half = Math.Max(80, ms / 2);

        await AnimateOpacityAsync(_incomingLayer, 1, 0, half).ConfigureAwait(true);

        await applyContentAsync().ConfigureAwait(true);
        if (_incomingLayer is FrameworkElement fe)
        {
            fe.UpdateLayout();
        }

        await AnimateOpacityAsync(_incomingLayer, 0, 1, half).ConfigureAwait(true);
        ResetVisualState();
    }

    /// <summary>Одновременный dissolve: старый слой гаснет, новый проявляется.</summary>
    private async Task RunCrossFadeAsync(Func<Task> applyContentAsync, int durationMs)
    {
        ResetCompositionOpacity(_incomingLayer);
        ResetCompositionOpacity(_outgoingLayer);

        _outgoingLayer.Opacity = 1;
        _incomingLayer.Opacity = 0;

        await applyContentAsync().ConfigureAwait(true);
        if (_incomingLayer is FrameworkElement fe)
        {
            fe.UpdateLayout();
        }

        var ms = Math.Clamp(durationMs <= 0 ? 750 : durationMs, 150, 3000);
        await CrossFadeAsync(_outgoingLayer, _incomingLayer, ms).ConfigureAwait(true);

        _outgoingLayer.Opacity = 0;
        _incomingLayer.Opacity = 1;
        ResetCompositionOpacity(_incomingLayer);
        ResetCompositionOpacity(_outgoingLayer);
    }

    private Task AnimateOpacityAsync(UIElement target, double from, double to, int durationMs)
    {
        StopActiveStoryboard();
        ResetCompositionOpacity(target);

        var tcs = new TaskCompletionSource();
        var sb = new Storyboard();
        _activeStoryboard = sb;

        var anim = new DoubleAnimation
        {
            From = from,
            To = to,
            Duration = new Duration(TimeSpan.FromMilliseconds(durationMs)),
            EnableDependentAnimation = true,
            EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseInOut }
        };

        Storyboard.SetTarget(anim, target);
        Storyboard.SetTargetProperty(anim, "Opacity");
        sb.Children.Add(anim);
        sb.Completed += (_, _) =>
        {
            target.Opacity = to;
            if (ReferenceEquals(_activeStoryboard, sb))
            {
                _activeStoryboard = null;
            }

            tcs.TrySetResult();
        };

        target.Opacity = from;
        sb.Begin();
        return tcs.Task;
    }

    private Task CrossFadeAsync(UIElement fadeOut, UIElement fadeIn, int durationMs)
    {
        StopActiveStoryboard();
        ResetCompositionOpacity(fadeOut);
        ResetCompositionOpacity(fadeIn);

        var tcs = new TaskCompletionSource();
        var sb = new Storyboard();
        _activeStoryboard = sb;
        var duration = new Duration(TimeSpan.FromMilliseconds(durationMs));
        var ease = new QuadraticEase { EasingMode = EasingMode.EaseInOut };

        var animOut = new DoubleAnimation
        {
            From = 1,
            To = 0,
            Duration = duration,
            EnableDependentAnimation = true,
            EasingFunction = ease
        };
        var animIn = new DoubleAnimation
        {
            From = 0,
            To = 1,
            Duration = duration,
            EnableDependentAnimation = true,
            EasingFunction = ease
        };

        Storyboard.SetTarget(animOut, fadeOut);
        Storyboard.SetTargetProperty(animOut, "Opacity");
        Storyboard.SetTarget(animIn, fadeIn);
        Storyboard.SetTargetProperty(animIn, "Opacity");
        sb.Children.Add(animOut);
        sb.Children.Add(animIn);
        sb.Completed += (_, _) =>
        {
            fadeOut.Opacity = 0;
            fadeIn.Opacity = 1;
            if (ReferenceEquals(_activeStoryboard, sb))
            {
                _activeStoryboard = null;
            }

            tcs.TrySetResult();
        };

        fadeOut.Opacity = 1;
        fadeIn.Opacity = 0;
        sb.Begin();
        return tcs.Task;
    }

    private void StopActiveStoryboard()
    {
        try
        {
            _activeStoryboard?.Stop();
        }
        catch
        {
            // ignore
        }

        _activeStoryboard = null;
    }

    public void ResetVisualState()
    {
        StopActiveStoryboard();
        ResetCompositionOpacity(_incomingLayer);
        ResetCompositionOpacity(_outgoingLayer);
        _incomingLayer.Opacity = 1;
        _outgoingLayer.Opacity = 0;
    }

    private static void ResetCompositionOpacity(UIElement element)
    {
        try
        {
            ElementCompositionPreview.GetElementVisual(element).Opacity = 1f;
        }
        catch
        {
            // ignore
        }
    }
}
