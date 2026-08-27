using ChyguiSlide.Data.Entities;
using ChyguiSlide.Services.Models;

namespace ChyguiSlide.Services.Abstractions;

public interface IProjectionDisplayService
{
    bool IsOpen { get; }
    bool IsBlackout { get; }
    bool IsNdiModeActive { get; }

    event EventHandler<bool>? ProjectionWindowVisibilityChanged;
    event EventHandler<bool>? BlackoutStateChanged;
    event EventHandler<bool>? NdiModeStateChanged;
    event EventHandler<MediaPlaybackStatus>? MediaStatusChanged;
    event EventHandler<string>? MediaPlaybackFailed;

    /// <summary>Сцена превью программы в Live Control (зеркало текущего слайда).</summary>
    UIElement? ProgramStage { get; }

    /// <summary>Привязать хост превью на странице трансляции.</summary>
    void BindProgramPreviewHost(Panel? host);

    Task ShowAsync();
    void Hide();
    void SetBlackout(bool isBlackout);
    /// <summary>Сброс opacity слоёв слайда и перерисовка текущего текста.</summary>
    void EnsureContentVisible();
    void ApplyTheme(ThemePreset? theme);
    Task ToggleVideoModeAsync();
    Task ToggleNdiVideoModeAsync();
    Task<List<NdiSource>> GetAvailableNdiSourcesAsync();

    void MediaPlay();
    void MediaPause();
    void MediaSeek(double positionSec);
    void MediaSetLoop(bool loop);

    /// <summary>
    /// Остановить нативный MediaPlayer/оверлей до ухода со страницы (иначе WinUI COM-краш).
    /// </summary>
    void StopForegroundMedia();
}
