using System.Collections.ObjectModel;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;

namespace ChyguiSlide.ViewModels;

public partial class MainViewModel : ObservableObject
{
    public MainViewModel()
    {
        NavigationItems = new ObservableCollection<ShellNavigationItem>(
            new[]
            {
                new ShellNavigationItem("Обзор", "layout-dashboard", typeof(Views.DashboardPage)),
                new ShellNavigationItem("Песни", "music", typeof(Views.CatalogPage)),
                new ShellNavigationItem("Библия", "book-open-text", typeof(Views.BiblePage)),
                new ShellNavigationItem("Объявления", "megaphone", typeof(Views.AnnouncementsPage)),
                new ShellNavigationItem("Трансляция", "radio", typeof(Views.LiveControlPage)),
                new ShellNavigationItem("Настройки", "settings", typeof(Views.SettingsPage)),
            });

        selectedItem = NavigationItems.FirstOrDefault();
    }

    public ObservableCollection<ShellNavigationItem> NavigationItems { get; }

    [ObservableProperty]
    private ShellNavigationItem? selectedItem;

    public bool IsOnCatalogPage => SelectedItem?.PageType == typeof(Views.CatalogPage);

    public bool IsOnBiblePage => SelectedItem?.PageType == typeof(Views.BiblePage);

    public bool IsOnAnnouncementsPage => SelectedItem?.PageType == typeof(Views.AnnouncementsPage);

    public bool IsOnLiveControlPage => SelectedItem?.PageType == typeof(Views.LiveControlPage);

    public void NavigateToLiveControl()
    {
        var liveItem = NavigationItems.FirstOrDefault(i => i.PageType == typeof(Views.LiveControlPage));
        if (liveItem is not null)
        {
            SelectedItem = liveItem;
        }
    }

    public void NavigateToBible()
    {
        var bibleItem = NavigationItems.FirstOrDefault(i => i.PageType == typeof(Views.BiblePage));
        if (bibleItem is not null)
        {
            SelectedItem = bibleItem;
        }
    }

    public void NavigateToCatalog()
    {
        var catalogItem = NavigationItems.FirstOrDefault(i => i.PageType == typeof(Views.CatalogPage));
        if (catalogItem is not null)
        {
            SelectedItem = catalogItem;
        }
    }
}
