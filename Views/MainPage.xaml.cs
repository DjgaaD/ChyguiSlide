using ChyguiSlide.Services.Abstractions;
using ChyguiSlide.Services.Implementations;
using ChyguiSlide.ViewModels;
using ChyguiSlide.Views.Dialogs;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media.Animation;

namespace ChyguiSlide.Views;

public sealed partial class MainPage : Page
{
    private readonly HotkeyDispatcher _hotkeyDispatcher;
    private bool _startupUpdateCheckStarted;
    private bool _modernNavStripeReady;

    public MainViewModel ViewModel { get; }

    public MainPage()
    {
        InitializeComponent();
        ViewModel = App.AppHost.Services.GetRequiredService<MainViewModel>();
        _hotkeyDispatcher = App.AppHost.Services.GetRequiredService<HotkeyDispatcher>();
        DataContext = ViewModel;
        ViewModel.PropertyChanged += OnViewModelPropertyChanged;
        Loaded += OnPageLoaded;
        Unloaded += OnPageUnloaded;
        ApplyModernChrome();
    }

    private void OnViewModelPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(MainViewModel.SelectedItem))
        {
            return;
        }

        // Горячие клавиши меняют SelectedItem без клика по NavigationView —
        // SelectionChanged может не прийти, поэтому навигируем явно.
        NavigateToSelection(ViewModel.SelectedItem, new SuppressNavigationTransitionInfo());
    }

    private void ApplyModernChrome()
    {
        ModernHeader.Visibility = Visibility.Visible;
        ShellNav.PaneDisplayMode = NavigationViewPaneDisplayMode.LeftMinimal;
        ShellNav.IsPaneToggleButtonVisible = false;
        ShellNav.IsPaneOpen = false;
        ShellNav.OpenPaneLength = 0;
        ShellNav.CompactPaneLength = 0;
    }

    private async void OnPageLoaded(object sender, RoutedEventArgs e)
    {
        try
        {
            var hotkeys = App.AppHost.Services.GetRequiredService<IHotkeyService>();
            await hotkeys.GetAllAsync();
        }
        catch
        {
            // defaults already in memory
        }

        var target = App.MainWindow?.Content as UIElement ?? this;
        _hotkeyDispatcher.AttachToMain(target);

        if (!_startupUpdateCheckStarted)
        {
            _startupUpdateCheckStarted = true;
            _ = CheckForUpdatesAfterStartupAsync();
        }
    }

    private async Task CheckForUpdatesAfterStartupAsync()
    {
        try
        {
            // Даём UI отрисоваться до сетевого запроса
            await Task.Delay(1200);
            if (XamlRoot is null)
            {
                return;
            }

            await AppUpdateDialog.CheckOnStartupAsync(XamlRoot);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[Update] {ex.Message}");
        }
    }

    private void OnPageUnloaded(object sender, RoutedEventArgs e)
    {
        _hotkeyDispatcher.DetachMain();
    }

    private void OnNavigationViewLoaded(object sender, RoutedEventArgs e)
    {
        ApplyModernChrome();
        ShellNav.SelectedItem = ViewModel.SelectedItem;
        NavigateToSelection(ViewModel.SelectedItem, new EntranceNavigationTransitionInfo());

        if (ModernNavList is not null)
        {
            ModernNavList.SelectedItem = ViewModel.SelectedItem;
            QueueModernNavStripeUpdate();
        }
    }

    private void OnModernNavItemClick(object sender, ItemClickEventArgs e)
    {
        if (e.ClickedItem is not ShellNavigationItem item)
        {
            return;
        }

        // Вне обработчика клика — иначе реентерабельный Navigate даёт E_ABORT / краш.
        _ = DispatcherQueue.TryEnqueue(() =>
        {
            try
            {
                ViewModel.SelectedItem = item;
            }
            catch (Exception ex)
            {
                ChyguiSlide.Data.InteractionLogger.Log(
                    $"MainPage.OnModernNavItemClick: {ex.Message}");
            }
        });
    }

    private void OnModernNavSelectionChanged(object sender, SelectionChangedEventArgs e)
        => QueueModernNavStripeUpdate();

    private void OnModernNavSizeChanged(object sender, SizeChangedEventArgs e)
        => QueueModernNavStripeUpdate();

    private void QueueModernNavStripeUpdate()
    {
        _ = DispatcherQueue.TryEnqueue(Microsoft.UI.Dispatching.DispatcherQueuePriority.Low, UpdateModernNavStripe);
    }

    private void UpdateModernNavStripe()
    {
        if (ModernNavList is null || ModernNavStripe is null || ModernNavStripeTransform is null || ModernNavHost is null)
        {
            return;
        }

        var selected = ViewModel.SelectedItem ?? ModernNavList.SelectedItem;
        var container = selected is null ? null : ModernNavList.ContainerFromItem(selected) as FrameworkElement;
        SelectionStripeHelper.MoveHorizontal(
            ModernNavStripe,
            ModernNavStripeTransform,
            container,
            ModernNavHost,
            animate: _modernNavStripeReady);
        if (container is not null)
        {
            _modernNavStripeReady = true;
        }
    }

    private void OnNavigationViewSelectionChanged(NavigationView sender, NavigationViewSelectionChangedEventArgs args)
    {
        var item = args.SelectedItem as ShellNavigationItem
                   ?? args.SelectedItemContainer?.DataContext as ShellNavigationItem
                   ?? ViewModel.SelectedItem;

        if (item is not null)
        {
            // Используем SuppressNavigationTransitionInfo для всех переходов, чтобы избежать мигания
            NavigateToSelection(item, new SuppressNavigationTransitionInfo());
        }
    }

    private void NavigateToSelection(ShellNavigationItem? item, NavigationTransitionInfo? transitionInfo = null)
    {
        if (item is null)
        {
            return;
        }

        var transition = transitionInfo ?? new SuppressNavigationTransitionInfo();

        // Сначала меняем SelectedItem, потом навигируем
        if (!ReferenceEquals(ShellNav.SelectedItem, item))
        {
            ShellNav.SelectedItem = item;
        }

        if (ModernNavList is not null && !ReferenceEquals(ModernNavList.SelectedItem, item))
        {
            ModernNavList.SelectedItem = item;
        }

        QueueModernNavStripeUpdate();

        if (ContentFrame.CurrentSourcePageType != item.PageType)
        {
            try
            {
                ContentFrame.Navigate(item.PageType, null, transition);
            }
            catch (Exception ex)
            {
                ChyguiSlide.Data.InteractionLogger.Log(
                    $"MainPage.NavigateToSelection: {ex.GetType().Name} {ex.Message}");
            }
        }
    }
}
