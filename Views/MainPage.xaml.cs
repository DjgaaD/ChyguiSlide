using ChyguiSlide.Services.Abstractions;
using ChyguiSlide.Services.Implementations;
using ChyguiSlide.ViewModels;
using ChyguiSlide.Views.Dialogs;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Animation;
using Windows.Storage;

namespace ChyguiSlide.Views;

public sealed partial class MainPage : Page
{
    private const string NavigationPaneOpenKey = "NavigationPaneIsOpen";
    private readonly HotkeyDispatcher _hotkeyDispatcher;
    private bool _startupUpdateCheckStarted;

    public MainViewModel ViewModel { get; }

    public MainPage()
    {
        InitializeComponent();
        ViewModel = App.AppHost.Services.GetRequiredService<MainViewModel>();
        _hotkeyDispatcher = App.AppHost.Services.GetRequiredService<HotkeyDispatcher>();
        DataContext = ViewModel;
        Loaded += OnPageLoaded;
        Unloaded += OnPageUnloaded;
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
        ShellNav.SelectedItem = ViewModel.SelectedItem;
        NavigateToSelection(ViewModel.SelectedItem, new EntranceNavigationTransitionInfo());

        RestoreNavigationPaneState();
        UpdateNavLabelVisibility(ShellNav.IsPaneOpen);

        ShellNav.DisplayModeChanged += OnNavigationViewDisplayModeChanged;
        ShellNav.PaneOpening += OnNavigationPaneOpening;
        ShellNav.PaneClosing += OnNavigationPaneClosing;
    }

    private void RestoreNavigationPaneState()
    {
        try
        {
            var localSettings = ApplicationData.Current.LocalSettings;
            if (localSettings.Values.TryGetValue(NavigationPaneOpenKey, out var value) && value is bool isOpen)
            {
                ShellNav.IsPaneOpen = isOpen;
            }
        }
        catch
        {
            // Игнорируем ошибки при восстановлении настроек
        }
    }

    private void SaveNavigationPaneState()
    {
        try
        {
            var localSettings = ApplicationData.Current.LocalSettings;
            localSettings.Values[NavigationPaneOpenKey] = ShellNav.IsPaneOpen;
        }
        catch
        {
            // Игнорируем ошибки при сохранении настроек
        }
    }

    private void OnNavigationViewDisplayModeChanged(NavigationView sender, NavigationViewDisplayModeChangedEventArgs args)
    {
        SaveNavigationPaneState();
    }

    private void OnNavigationPaneOpening(NavigationView sender, object args)
    {
        SaveNavigationPaneState();
        UpdateNavLabelVisibility(true);
    }

    private void OnNavigationPaneClosing(NavigationView sender, NavigationViewPaneClosingEventArgs args)
    {
        SaveNavigationPaneState();
        UpdateNavLabelVisibility(false);
    }

    private void UpdateNavLabelVisibility(bool visible)
    {
        var visibility = visible ? Visibility.Visible : Visibility.Collapsed;
        foreach (var item in ViewModel.NavigationItems)
        {
            if (ShellNav.ContainerFromMenuItem(item) is not NavigationViewItem container)
            {
                continue;
            }

            SetNavLabels(container, visibility);
        }
    }

    private static void SetNavLabels(DependencyObject root, Visibility visibility)
    {
        var count = VisualTreeHelper.GetChildrenCount(root);
        for (var i = 0; i < count; i++)
        {
            var child = VisualTreeHelper.GetChild(root, i);
            if (child is TextBlock { Tag: "NavLabel" } label)
            {
                label.Visibility = visibility;
            }

            SetNavLabels(child, visibility);
        }
    }

    private void OnNavigationViewSelectionChanged(NavigationView sender, NavigationViewSelectionChangedEventArgs args)
    {
        var item = args.SelectedItem as ShellNavigationItem
                   ?? args.SelectedItemContainer?.DataContext as ShellNavigationItem
                   ?? ViewModel.SelectedItem;

        if (item is not null)
        {
            NavigateToSelection(item, args.RecommendedNavigationTransitionInfo);
        }
    }

    private void NavigateToSelection(ShellNavigationItem? item, NavigationTransitionInfo? transitionInfo = null)
    {
        if (item is null)
        {
            return;
        }

        if (!ReferenceEquals(ShellNav.SelectedItem, item))
        {
            ShellNav.SelectedItem = item;
        }

        var transition = transitionInfo ?? new SuppressNavigationTransitionInfo();

        if (ContentFrame.CurrentSourcePageType != item.PageType)
        {
            ContentFrame.Navigate(item.PageType, null, transition);
        }
    }
}
