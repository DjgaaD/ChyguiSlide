using ChyguiSlide.Services.Models;

namespace ChyguiSlide.ViewModels;

public sealed partial class TransitionStyleOptionItem
{
    public TransitionStyle Style { get; }
    public string Title { get; }
    public string Description { get; }

    public TransitionStyleOptionItem(TransitionStyle style)
    {
        Style = style;
        Title = style.GetTitle();
        Description = style.GetDescription();
    }
}
