using CommunityToolkit.Mvvm.ComponentModel;
using ChyguiSlide.Services.Models;

namespace ChyguiSlide.ViewModels;

public sealed partial class SectionTransitionOptionItem : ObservableObject
{
    public SectionTransitionMode Mode { get; }
    public string Title { get; }
    public string Description { get; }

    public SectionTransitionOptionItem(SectionTransitionMode mode)
    {
        Mode = mode;
        Title = mode.GetTitle();
        Description = mode.GetDescription();
    }
}
