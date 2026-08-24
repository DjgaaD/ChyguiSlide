using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;

namespace ChyguiSlide.Views.UiAnimation;

/// <summary>
/// Вертикальная полоса списка (слайды): общая логика для Трансляции, Песен и следующих разделов.
/// Основа — поведение раздела «Трансляция».
/// </summary>
internal sealed class ListSelectionStripeBinder
{
    private readonly ListView _list;
    private readonly FrameworkElement _stripe;
    private readonly UIElement _host;
    private readonly Func<object?> _getSelected;
    private readonly DispatcherQueue _dispatcher;
    private readonly Func<object, bool>? _isSelectedItem;

    private bool _ready;
    private bool _animating;
    private object? _target;
    private int _animToken;
    private int _retryCount;
    private ScrollViewer? _scroll;
    private bool _attached;
    private FrameworkElement? _hostElement;

    public ListSelectionStripeBinder(
        ListView list,
        FrameworkElement stripe,
        UIElement host,
        Func<object?> getSelected,
        DispatcherQueue dispatcher,
        Func<object, bool>? isSelectedItem = null)
    {
        _list = list;
        _stripe = stripe;
        _host = host;
        _getSelected = getSelected;
        _dispatcher = dispatcher;
        _isSelectedItem = isSelectedItem;
    }

    public void Attach()
    {
        if (_attached)
        {
            return;
        }

        _attached = true;
        _list.SelectionChanged += OnSelectionChanged;
        _list.SizeChanged += OnSizeChanged;
        _list.ContainerContentChanging += OnContainerContentChanging;
        if (_host is FrameworkElement hostElement)
        {
            _hostElement = hostElement;
            hostElement.SizeChanged += OnHostSizeChanged;
            ApplyHostClip();
        }

        EnsureScrollHook();
        RequestUpdate(animate: false);
    }

    public void Detach()
    {
        if (!_attached)
        {
            return;
        }

        _attached = false;
        _list.SelectionChanged -= OnSelectionChanged;
        _list.SizeChanged -= OnSizeChanged;
        _list.ContainerContentChanging -= OnContainerContentChanging;
        if (_hostElement is not null)
        {
            _hostElement.SizeChanged -= OnHostSizeChanged;
            _hostElement = null;
        }

        if (_scroll is not null)
        {
            _scroll.ViewChanged -= OnViewChanged;
        }
    }

    public void ResetReady()
    {
        _ready = false;
        _retryCount = 0;
        RequestUpdate(animate: false);
    }

    public void RequestUpdate(bool animate)
    {
        if (!_attached)
        {
            return;
        }

        _ = _dispatcher.TryEnqueue(DispatcherQueuePriority.Low, () => Update(animate));
    }

    public void ScrollSelectedIntoViewIfNeeded()
    {
        var selected = _getSelected() ?? _list.SelectedItem;
        if (selected is not null)
        {
            TryScrollIfNeeded(selected);
        }
    }

    private void OnSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        _retryCount = 0;
        RequestUpdate(animate: true);
    }

    private void OnSizeChanged(object sender, SizeChangedEventArgs e)
    {
        if (!_animating)
        {
            RequestUpdate(animate: false);
        }
    }

    private void OnContainerContentChanging(ListViewBase sender, ContainerContentChangingEventArgs args)
    {
        if (_animating || args.Item is null)
        {
            return;
        }

        var selected = _getSelected();
        var isSelected = _isSelectedItem is not null
            ? _isSelectedItem(args.Item)
            : ReferenceEquals(args.Item, selected);
        if (isSelected)
        {
            RequestUpdate(animate: _ready);
        }
    }

    private void OnHostSizeChanged(object sender, SizeChangedEventArgs e)
    {
        ApplyHostClip();
        if (!_animating)
        {
            RequestUpdate(animate: false);
        }
    }

    private void ApplyHostClip()
    {
        if (_hostElement is null || _hostElement.ActualWidth < 1 || _hostElement.ActualHeight < 1)
        {
            return;
        }

        _hostElement.Clip = new RectangleGeometry
        {
            Rect = new global::Windows.Foundation.Rect(0, 0, _hostElement.ActualWidth, _hostElement.ActualHeight)
        };
    }

    private void OnViewChanged(object? sender, ScrollViewerViewChangedEventArgs e)
    {
        RequestUpdate(animate: false);
    }

    private void EnsureScrollHook()
    {
        if (_scroll is not null)
        {
            return;
        }

        _scroll = FindDescendant<ScrollViewer>(_list);
        if (_scroll is not null)
        {
            _scroll.ViewChanged += OnViewChanged;
        }
    }

    private void Update(bool animate)
    {
        EnsureScrollHook();
        var selected = _getSelected() ?? _list.SelectedItem;
        if (_animating && animate && ReferenceEquals(_target, selected))
        {
            return;
        }

        var container = selected is null ? null : _list.ContainerFromItem(selected) as FrameworkElement;
        if (selected is not null && (container is null || container.ActualHeight < 1) && _retryCount < 6)
        {
            _retryCount++;
            TryScrollIfNeeded(selected);
            _ = _dispatcher.TryEnqueue(DispatcherQueuePriority.Low, () => Update(animate: true));
            return;
        }

        var shouldAnimate = animate && _ready;
        if (shouldAnimate
            && container is not null
            && !IsFullyVisible(container))
        {
            TryScrollIfNeeded(selected!);
            shouldAnimate = false;
        }

        if (shouldAnimate)
        {
            var token = ++_animToken;
            _animating = true;
            _target = selected;
            var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(300) };
            timer.Tick += (_, _) =>
            {
                timer.Stop();
                if (token != _animToken)
                {
                    return;
                }

                _animating = false;
                Update(animate: false);
            };
            timer.Start();
        }

        SelectionStripeMotion.MoveVertical(_stripe, container, _host, shouldAnimate);
        if (container is not null && container.ActualHeight >= 1)
        {
            _ready = true;
            _retryCount = 0;
        }
    }

    private void TryScrollIfNeeded(object current)
    {
        var container = _list.ContainerFromItem(current) as FrameworkElement;
        if (container is not null && IsFullyVisible(container))
        {
            return;
        }

        _list.ScrollIntoView(current, ScrollIntoViewAlignment.Default);
    }

    private bool IsFullyVisible(FrameworkElement container)
    {
        EnsureScrollHook();
        if (_scroll is null || container.ActualHeight < 1)
        {
            return false;
        }

        try
        {
            var bounds = container.TransformToVisual(_scroll)
                .TransformBounds(new global::Windows.Foundation.Rect(0, 0, container.ActualWidth, container.ActualHeight));
            return bounds.Y >= -1
                && bounds.Y + bounds.Height <= _scroll.ViewportHeight + 1;
        }
        catch
        {
            return false;
        }
    }

    private static T? FindDescendant<T>(DependencyObject root) where T : DependencyObject
    {
        var count = VisualTreeHelper.GetChildrenCount(root);
        for (var i = 0; i < count; i++)
        {
            var child = VisualTreeHelper.GetChild(root, i);
            if (child is T match)
            {
                return match;
            }

            var nested = FindDescendant<T>(child);
            if (nested is not null)
            {
                return nested;
            }
        }

        return null;
    }
}
