using ChyguiSlide.Data.Enums;

namespace ChyguiSlide.ViewModels;

public sealed record BackgroundPickModeOptionItem(
    ThemeBackgroundPickMode Mode,
    string Title,
    string Description);

public sealed record WallpaperPoolOptionItem(
    ThemeWallpaperPool Pool,
    string Title);
