using ChyguiSlide.Services.Models;

namespace ChyguiSlide.ViewModels;

public sealed record BiblePickerLayoutOptionItem(
    BiblePickerLayoutMode Mode,
    string Title,
    string Description);
