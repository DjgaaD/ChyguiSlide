using System;
using System.Collections.Specialized;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media.Animation;

namespace ChyguiSlide.Views.UiAnimation;

/// <summary>
/// Отключает стандартный «выезд» элементов ListView/GridView и при смене набора
/// показывает содержимое через короткий fade (как Crossfade на проекторе).
/// </summary>
public static class ItemListAppearance
{
    private const int FadeMs = 200;

    public static readonly DependencyProperty UseCrossfadeProperty =
        DependencyProperty.RegisterAttached(
            "UseCrossfade",
            typeof(bool),
            typeof(ItemListAppearance),
            new PropertyMetadata(false, OnUseCrossfadeChanged));

    public static void SetUseCrossfade(DependencyObject element, bool value)
        => element.SetValue(UseCrossfadeProperty, value);

    public static bool GetUseCrossfade(DependencyObject element)
        => (bool)element.GetValue(UseCrossfadeProperty);

    private static readonly DependencyProperty FadeTokenProperty =
        DependencyProperty.RegisterAttached(
            "FadeToken",
            typeof(int),
            typeof(ItemListAppearance),
            new PropertyMetadata(0));

    private static readonly DependencyProperty CollectionHookProperty =
        DependencyProperty.RegisterAttached(
            "CollectionHook",
            typeof(CollectionHook),
            typeof(ItemListAppearance),
            new PropertyMetadata(null));

    private static readonly DependencyProperty ItemsSourceCallbackTokenProperty =
        DependencyProperty.RegisterAttached(
            "ItemsSourceCallbackToken",
            typeof(long),
            typeof(ItemListAppearance),
            new PropertyMetadata(0L));

    private static readonly DependencyProperty HasPlayedProperty =
        DependencyProperty.RegisterAttached(
            "HasPlayed",
            typeof(bool),
            typeof(ItemListAppearance),
            new PropertyMetadata(false));

    private static void OnUseCrossfadeChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not ListViewBase list)
        {
            return;
        }

        if (e.NewValue is true)
        {
            SuppressEntranceSlide(list);
            list.Loaded -= OnListLoaded;
            list.Loaded += OnListLoaded;
            list.Unloaded -= OnListUnloaded;
            list.Unloaded += OnListUnloaded;
            if ((long)list.GetValue(ItemsSourceCallbackTokenProperty) == 0)
            {
                var token = list.RegisterPropertyChangedCallback(
                    ItemsControl.ItemsSourceProperty,
                    OnItemsSourceChanged);
                list.SetValue(ItemsSourceCallbackTokenProperty, token);
            }

            HookCollection(list);
        }
        else
        {
            UnhookCollection(list);
        }
    }

    private static void OnListLoaded(object sender, RoutedEventArgs e)
    {
        if (sender is ListViewBase list)
        {
            SuppressEntranceSlide(list);
            HookCollection(list);
        }
    }

    private static void OnListUnloaded(object sender, RoutedEventArgs e)
    {
        if (sender is ListViewBase list)
        {
            UnhookCollection(list);
        }
    }

    private static void OnItemsSourceChanged(DependencyObject sender, DependencyProperty dp)
    {
        if (sender is ListViewBase list)
        {
            HookCollection(list);
            ScheduleFadeIn(list);
        }
    }

    private static void SuppressEntranceSlide(ListViewBase list)
    {
        list.ItemContainerTransitions = new TransitionCollection();
        list.Transitions = new TransitionCollection();
    }

    private static void HookCollection(ListViewBase list)
    {
        UnhookCollection(list);
        if (list.ItemsSource is not INotifyCollectionChanged notify)
        {
            return;
        }

        NotifyCollectionChangedEventHandler handler = (_, args) =>
        {
            if (ShouldFade(args, list.Items.Count))
            {
                ScheduleFadeIn(list);
            }
        };
        notify.CollectionChanged += handler;
        list.SetValue(CollectionHookProperty, new CollectionHook(notify, handler));
    }

    private static void UnhookCollection(ListViewBase list)
    {
        if (list.GetValue(CollectionHookProperty) is CollectionHook hook)
        {
            hook.Source.CollectionChanged -= hook.Handler;
            list.ClearValue(CollectionHookProperty);
        }
    }

    private static bool ShouldFade(NotifyCollectionChangedEventArgs e, int itemCount)
    {
        if (e.Action is NotifyCollectionChangedAction.Reset or NotifyCollectionChangedAction.Replace)
        {
            return true;
        }

        var added = e.NewItems?.Count ?? 0;
        return e.Action is NotifyCollectionChangedAction.Add && itemCount <= added + 1;
    }

    private static void ScheduleFadeIn(ListViewBase list)
    {
        if (!(bool)list.GetValue(HasPlayedProperty))
        {
            list.SetValue(HasPlayedProperty, true);
            list.Opacity = 1;
            return;
        }

        var token = (int)list.GetValue(FadeTokenProperty) + 1;
        list.SetValue(FadeTokenProperty, token);
        list.Opacity = 0;

        list.DispatcherQueue.TryEnqueue(DispatcherQueuePriority.Low, () =>
        {
            if ((int)list.GetValue(FadeTokenProperty) != token)
            {
                return;
            }

            PlayFadeIn(list);
        });
    }

    private static void PlayFadeIn(UIElement target)
    {
        var animation = new DoubleAnimation
        {
            From = 0,
            To = 1,
            Duration = TimeSpan.FromMilliseconds(FadeMs),
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
        };
        Storyboard.SetTarget(animation, target);
        Storyboard.SetTargetProperty(animation, "Opacity");
        var board = new Storyboard();
        board.Children.Add(animation);
        board.Begin();
    }

    private sealed class CollectionHook
    {
        public CollectionHook(INotifyCollectionChanged source, NotifyCollectionChangedEventHandler handler)
        {
            Source = source;
            Handler = handler;
        }

        public INotifyCollectionChanged Source { get; }
        public NotifyCollectionChangedEventHandler Handler { get; }
    }
}
