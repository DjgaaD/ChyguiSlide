using ChyguiSlide.Data.Entities;

namespace ChyguiSlide.Services.Abstractions;

public interface IProjectionDisplayService
{
    bool IsOpen { get; }
    bool IsBlackout { get; }
    bool IsNdiModeActive { get; }

    event EventHandler<bool>? ProjectionWindowVisibilityChanged;
    event EventHandler<bool>? BlackoutStateChanged;
    event EventHandler<bool>? NdiModeStateChanged;

    Task ShowAsync();
    void Hide();
    void SetBlackout(bool isBlackout);
    void ApplyTheme(ThemePreset? theme);
    Task ToggleVideoModeAsync();
    Task ToggleNdiVideoModeAsync();
    Task<List<NdiSource>> GetAvailableNdiSourcesAsync();
}


