using ChyguiSlide.Services.Models;

namespace ChyguiSlide.ViewModels;

public sealed record NavigationPaneOptionItem(
    NavigationPaneMode Mode,
    string Title,
    string Description);
