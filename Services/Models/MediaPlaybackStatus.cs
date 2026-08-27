namespace ChyguiSlide.Services.Models;

public sealed record MediaPlaybackStatus(
    double PositionSec,
    double DurationSec,
    bool IsPaused);
