using CommunityToolkit.Mvvm.ComponentModel;

namespace ChyguiSlide.ViewModels;

/// <summary>Слайд объявления в панели деталей (как секция песни в каталоге).</summary>
public sealed partial class AnnouncementSlidePreviewItem : ObservableObject
{
    public AnnouncementSlidePreviewItem(int listIndex, string title, string content)
    {
        ListIndex = listIndex;
        Title = title;
        Content = content;
    }

    public int ListIndex { get; }
    public string Title { get; }
    public string Content { get; }

    [ObservableProperty]
    private bool isSelected;
}
