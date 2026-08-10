using ChyguiSlide.Services.Models;

namespace ChyguiSlide.ViewModels;

public sealed class BibleReferencePlacementItem
{
    public BibleReferencePlacementItem(BibleReferencePlacement placement, string title)
    {
        Placement = placement;
        Title = title;
    }

    public BibleReferencePlacement Placement { get; }
    public string Title { get; }
}
