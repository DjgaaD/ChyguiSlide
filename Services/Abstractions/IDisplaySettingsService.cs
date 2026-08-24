using System;
using ChyguiSlide.Services.Models;

namespace ChyguiSlide.Services.Abstractions;

public interface IDisplaySettingsService
{
    Task<IReadOnlyList<DisplayInfo>> GetAvailableDisplaysAsync();
    Task<string?> GetSelectedDisplayIdAsync();
    Task SetSelectedDisplayIdAsync(string? displayId);
    Task<DisplayInfo?> GetSelectedDisplayAsync();
    Task<Guid?> GetSelectedThemePresetIdAsync();
    Task SetSelectedThemePresetIdAsync(Guid? themePresetId);
    Task<bool> GetWordWrapAsync();
    Task SetWordWrapAsync(bool wordWrap);
    Task<TextLayoutMode> GetTextLayoutModeAsync();
    Task SetTextLayoutModeAsync(TextLayoutMode mode);
    Task<string?> GetCameraHostAsync();
    Task SetCameraHostAsync(string? host);
    Task<int> GetCameraPortAsync();
    Task SetCameraPortAsync(int port);
    Task<string?> GetNdiSourceNameAsync();
    Task SetNdiSourceNameAsync(string? sourceName);
    Task<HotkeyBinding?> GetHotkeyAsync(AppHotkeyAction action);
    Task SetHotkeyAsync(AppHotkeyAction action, HotkeyBinding? binding);
    Task<CatalogSortMode> GetCatalogSortModeAsync();
    Task SetCatalogSortModeAsync(CatalogSortMode mode);
    Task<bool> GetShowBibleReferenceAsync();
    Task SetShowBibleReferenceAsync(bool show);
    Task<bool> GetKeepProjectionBackgroundAsync();
    Task SetKeepProjectionBackgroundAsync(bool keep);
    /// <summary>
    /// Постоянный фон разрешён только при ≥2 мониторах и выбранном не основном экране.
    /// </summary>
    Task<bool> CanKeepProjectionBackgroundAsync();
    Task<BibleReferencePlacement> GetBibleReferencePlacementAsync();
    Task SetBibleReferencePlacementAsync(BibleReferencePlacement placement);
    Task<string> GetBibleReferenceAlignmentAsync();
    Task SetBibleReferenceAlignmentAsync(string alignment);
    Task<AppUiThemeMode> GetAppUiThemeAsync();
    Task SetAppUiThemeAsync(AppUiThemeMode mode);
    Task<bool> GetAskBeforeCloseAsync();
    Task SetAskBeforeCloseAsync(bool ask);

    Task<bool> GetObsStreamEnabledAsync();
    Task SetObsStreamEnabledAsync(bool enabled);
    Task<int> GetObsStreamPortAsync();
    Task SetObsStreamPortAsync(int port);
    Task<bool> GetObsStreamBackdropEnabledAsync();
    Task SetObsStreamBackdropEnabledAsync(bool enabled);
    Task<double> GetObsStreamBackdropOpacityAsync();
    Task SetObsStreamBackdropOpacityAsync(double opacity);

    Task<int> GetProjectionMarginLeftAsync();
    Task SetProjectionMarginLeftAsync(int pixels);
    Task<int> GetProjectionMarginRightAsync();
    Task SetProjectionMarginRightAsync(int pixels);
    Task<int> GetProjectionMarginTopAsync();
    Task SetProjectionMarginTopAsync(int pixels);
    Task<int> GetProjectionMarginBottomAsync();
    Task SetProjectionMarginBottomAsync(int pixels);
}

