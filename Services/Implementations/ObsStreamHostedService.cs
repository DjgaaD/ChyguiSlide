using ChyguiSlide.Data;
using ChyguiSlide.Services.Abstractions;
using Microsoft.Extensions.Hosting;

namespace ChyguiSlide.Services.Implementations;

/// <summary>Запускает LAN-сервер OBS при старте приложения согласно настройкам.</summary>
public sealed class ObsStreamHostedService : IHostedService
{
    private readonly IObsStreamService _obsStream;
    private readonly IDisplaySettingsService _displaySettings;
    private readonly ObsProjectionBridge _bridge;

    public ObsStreamHostedService(
        IObsStreamService obsStream,
        IDisplaySettingsService displaySettings,
        ObsProjectionBridge bridge)
    {
        _obsStream = obsStream;
        _displaySettings = displaySettings;
        _bridge = bridge;
    }

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        _ = _bridge;

        var enabled = await _displaySettings.GetObsStreamEnabledAsync().ConfigureAwait(false);
        var port = await _displaySettings.GetObsStreamPortAsync().ConfigureAwait(false);

        try
        {
            await _obsStream.ApplySettingsAsync(enabled, port, cancellationToken).ConfigureAwait(false);
            var backdropEnabled = await _displaySettings.GetObsStreamBackdropEnabledAsync().ConfigureAwait(false);
            var backdropOpacity = await _displaySettings.GetObsStreamBackdropOpacityAsync().ConfigureAwait(false);
            _obsStream.ApplyBackdropSettings(backdropEnabled, backdropOpacity);
            InteractionLogger.Log($"[ObsStream] Hosted init: enabled={enabled}, port={port}, running={_obsStream.IsRunning}");
        }
        catch (Exception ex)
        {
            InteractionLogger.Log($"[ObsStream] Hosted init failed: {ex.Message}");
            System.Diagnostics.Debug.WriteLine($"[ObsStreamHostedService] Start failed: {ex.Message}");
        }
    }

    public Task StopAsync(CancellationToken cancellationToken)
        => _obsStream.ApplySettingsAsync(false, _obsStream.Port, cancellationToken);
}
