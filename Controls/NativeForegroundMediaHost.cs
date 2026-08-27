using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using ChyguiSlide.Services.Models;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media.Imaging;
using Windows.Media.Core;
using Windows.Media.Playback;
using Windows.Storage;

namespace ChyguiSlide.Controls;

/// <summary>
/// Foreground playlist media (photo/video) as a native layer over WebView2.
/// WebView continues to render slides and theme background underneath.
/// </summary>
public sealed class NativeForegroundMediaHost : IDisposable
{
    private readonly SemaphoreSlim _showGate = new(1, 1);

    private MediaPlayerElement? _video;
    private Image? _image;
    private MediaPlayer? _player;
    private DispatcherQueueTimer? _statusTimer;
    private bool _emitStatus;
    private bool _disposed;
    private bool _wantAutoPlay;
    private bool _failureReported;
    private string? _currentPath;
    private bool _isVideoActive;
    private int _showGeneration;

    public event EventHandler<MediaPlaybackStatus>? StatusChanged;

    public event EventHandler<string>? PlaybackFailed;

    public bool IsVideoActive => _isVideoActive && !string.IsNullOrWhiteSpace(_currentPath);

    public void Attach(MediaPlayerElement video, Image image, bool emitPlaybackStatus)
    {
        _video = video ?? throw new ArgumentNullException(nameof(video));
        _image = image ?? throw new ArgumentNullException(nameof(image));
        _emitStatus = emitPlaybackStatus;
        _video.AutoPlay = false;
    }

    public async Task ShowAsync(string path, bool isVideo, bool loop, bool autoPlay)
    {
        await _showGate.WaitAsync().ConfigureAwait(true);
        var generation = Interlocked.Increment(ref _showGeneration);
        try
        {
            await ShowCoreAsync(generation, path, isVideo, loop, autoPlay).ConfigureAwait(true);
        }
        finally
        {
            _showGate.Release();
        }
    }

    private async Task ShowCoreAsync(int generation, string path, bool isVideo, bool loop, bool autoPlay)
    {
        if (_disposed || _video is null || _image is null)
        {
            return;
        }

        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
        {
            ChyguiSlide.Data.InteractionLogger.Log(
                $"NativeForegroundMediaHost: file missing '{path}'");
            Hide();
            return;
        }

        if (string.Equals(_currentPath, path, StringComparison.OrdinalIgnoreCase)
            && isVideo == _isVideoActive
            && _player is not null)
        {
            SafeSetLoop(loop);
            _wantAutoPlay = autoPlay;
            if (autoPlay && isVideo)
            {
                SafePlay("same-path");
            }

            return;
        }

        // Снимаем только текущий плеер этой операции — не трогаем более новый Show.
        ReleasePlayer(disposeIfMatches: _player);

        if (!IsCurrent(generation))
        {
            return;
        }

        _currentPath = path;
        _isVideoActive = isVideo;
        _wantAutoPlay = autoPlay;
        _failureReported = false;

        if (!isVideo)
        {
            StopStatusTimer();
            CollapseVideoElement();

            try
            {
                _image.Source = new BitmapImage(new Uri(path));
                _image.Visibility = Visibility.Visible;
                ChyguiSlide.Data.InteractionLogger.Log(
                    $"NativeForegroundMediaHost: image shown {Path.GetFileName(path)}");
            }
            catch (Exception ex)
            {
                ReportFailure(path, $"image load failed: {ex.Message}");
                Hide();
            }

            return;
        }

        _image.Visibility = Visibility.Collapsed;
        _image.Source = null;
        _video.Visibility = Visibility.Visible;
        _video.AreTransportControlsEnabled = false;

        MediaPlayer? player = null;
        try
        {
            player = new MediaPlayer
            {
                IsLoopingEnabled = loop,
                IsMuted = false,
                AutoPlay = false,
            };
            player.CommandManager.IsEnabled = false;
            player.MediaOpened += OnPlayerMediaOpened;
            player.MediaFailed += OnPlayerMediaFailed;

            if (!IsCurrent(generation))
            {
                try
                {
                    player.MediaOpened -= OnPlayerMediaOpened;
                    player.MediaFailed -= OnPlayerMediaFailed;
                }
                catch
                {
                    // ignore
                }

                SafeClearAndDispose(player);
                return;
            }

            _player = player;
            _video.SetMediaPlayer(player);

            var file = await StorageFile.GetFileFromPathAsync(path).AsTask().ConfigureAwait(false);
            if (!IsCurrent(generation))
            {
                ReleasePlayer(disposeIfMatches: player);
                return;
            }

            await EnqueueAsync(() =>
            {
                if (!IsCurrent(generation) || !ReferenceEquals(_player, player))
                {
                    return;
                }

                try
                {
                    player.Source = MediaSource.CreateFromStorageFile(file);
                }
                catch (Exception ex)
                {
                    ReportFailure(path, $"set source failed: {ex.Message}");
                }
            }).ConfigureAwait(true);

            if (!IsCurrent(generation))
            {
                ReleasePlayer(disposeIfMatches: player);
                return;
            }

            ChyguiSlide.Data.InteractionLogger.Log(
                $"NativeForegroundMediaHost: source set {Path.GetFileName(path)} ({new FileInfo(path).Length / (1024 * 1024)} MB)");
            StartStatusTimer();
        }
        catch (Exception ex)
        {
            ReportFailure(path, $"open failed: {ex.Message}");
            ReleasePlayer(disposeIfMatches: player);
            CollapseVideoElement();
        }
    }

    public void Hide()
    {
        Interlocked.Increment(ref _showGeneration);
        StopStatusTimer();
        _currentPath = null;
        _isVideoActive = false;
        _wantAutoPlay = false;
        _failureReported = false;
        ReleasePlayer(disposeIfMatches: _player);
        CollapseVideoElement();

        if (_image is not null)
        {
            _image.Visibility = Visibility.Collapsed;
            _image.Source = null;
        }
    }

    public void Play()
    {
        if (!IsVideoActive)
        {
            return;
        }

        _wantAutoPlay = true;
        SafePlay("manual");
        EmitStatus();
    }

    public void Pause()
    {
        if (!IsVideoActive || _player is null)
        {
            return;
        }

        _wantAutoPlay = false;
        SafePause();
        EmitStatus();
    }

    public void Seek(double positionSec)
    {
        if (!IsVideoActive || _player is null)
        {
            return;
        }

        try
        {
            _player.PlaybackSession.Position = TimeSpan.FromSeconds(Math.Max(0, positionSec));
        }
        catch (Exception ex)
        {
            ChyguiSlide.Data.InteractionLogger.Log(
                $"NativeForegroundMediaHost: Seek failed: {ex.Message}");
        }

        EmitStatus();
    }

    public void SetLoop(bool loop) => SafeSetLoop(loop);

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        Hide();
        try
        {
            _showGate.Dispose();
        }
        catch
        {
            // ignore
        }
    }

    private bool IsCurrent(int generation) => !_disposed && generation == _showGeneration;

    private void CollapseVideoElement()
    {
        if (_video is null)
        {
            return;
        }

        try
        {
            _video.Visibility = Visibility.Collapsed;
            _video.SetMediaPlayer(null);
            _video.Source = null;
        }
        catch (Exception ex)
        {
            ChyguiSlide.Data.InteractionLogger.Log(
                $"NativeForegroundMediaHost: CollapseVideoElement: {ex.Message}");
        }
    }

    private void ReleasePlayer(MediaPlayer? disposeIfMatches)
    {
        var player = _player;
        if (player is null)
        {
            return;
        }

        if (disposeIfMatches is not null && !ReferenceEquals(player, disposeIfMatches))
        {
            return;
        }

        _player = null;
        Unsubscribe(player);
        SafeClearAndDispose(player);

        try
        {
            _video?.SetMediaPlayer(null);
        }
        catch
        {
            // ignore
        }
    }

    private void Unsubscribe(MediaPlayer player)
    {
        try
        {
            player.MediaOpened -= OnPlayerMediaOpened;
            player.MediaFailed -= OnPlayerMediaFailed;
        }
        catch
        {
            // ignore
        }
    }

    private static void SafeClearAndDispose(MediaPlayer player)
    {
        try
        {
            player.Pause();
        }
        catch
        {
            // ignore — player may already be failing
        }

        try
        {
            player.Source = null;
        }
        catch
        {
            // ignore
        }

        // Dispose откладываем: синхронный Dispose во время Navigate/Unloaded → COM 0x80004004.
        var dispatcher = DispatcherQueue.GetForCurrentThread();
        if (dispatcher is not null
            && dispatcher.TryEnqueue(DispatcherQueuePriority.Low, () =>
            {
                try
                {
                    player.Dispose();
                }
                catch
                {
                    // ignore
                }
            }))
        {
            return;
        }

        try
        {
            player.Dispose();
        }
        catch
        {
            // ignore
        }
    }

    private void SafePlay(string reason)
    {
        var player = _player;
        if (player is null || !_wantAutoPlay)
        {
            return;
        }

        try
        {
            player.Play();
            ChyguiSlide.Data.InteractionLogger.LogVerbose(
                $"NativeForegroundMediaHost: Play({reason}) state={player.PlaybackSession.PlaybackState}");
        }
        catch (Exception ex)
        {
            ChyguiSlide.Data.InteractionLogger.LogVerbose(
                $"NativeForegroundMediaHost: Play({reason}) failed: {ex.Message}");
            return;
        }

        SchedulePlayRetries(reason);
    }

    private void SafePause()
    {
        try
        {
            _player?.Pause();
        }
        catch (Exception ex)
        {
            ChyguiSlide.Data.InteractionLogger.Log(
                $"NativeForegroundMediaHost: Pause failed: {ex.Message}");
        }
    }

    private void SafeSetLoop(bool loop)
    {
        try
        {
            if (_player is not null)
            {
                _player.IsLoopingEnabled = loop;
            }
        }
        catch (Exception ex)
        {
            ChyguiSlide.Data.InteractionLogger.Log(
                $"NativeForegroundMediaHost: SetLoop failed: {ex.Message}");
        }
    }

    private void SchedulePlayRetries(string reason)
    {
        var generation = _showGeneration;
        foreach (var delayMs in new[] { 100, 300, 800 })
        {
            _ = Task.Run(async () =>
            {
                await Task.Delay(delayMs).ConfigureAwait(false);
                _video?.DispatcherQueue?.TryEnqueue(DispatcherQueuePriority.Normal, () =>
                {
                    if (!IsCurrent(generation) || _player is null || !_wantAutoPlay || _failureReported)
                    {
                        return;
                    }

                    try
                    {
                        if (_player.PlaybackSession.PlaybackState != MediaPlaybackState.Playing)
                        {
                            _player.Play();
                            ChyguiSlide.Data.InteractionLogger.Log(
                                $"NativeForegroundMediaHost: Play retry +{delayMs}ms ({reason}) state={_player.PlaybackSession.PlaybackState}");
                        }
                    }
                    catch (Exception ex)
                    {
                        ChyguiSlide.Data.InteractionLogger.LogVerbose(
                            $"NativeForegroundMediaHost: Play retry failed: {ex.Message}");
                    }
                });
            });
        }
    }

    private void OnPlayerMediaOpened(MediaPlayer sender, object args)
    {
        if (!ReferenceEquals(sender, _player) || _failureReported)
        {
            return;
        }

        try
        {
            var duration = sender.NaturalDuration.TotalSeconds;
            ChyguiSlide.Data.InteractionLogger.Log(
                $"NativeForegroundMediaHost: opened {Path.GetFileName(_currentPath)} duration={duration:F1}s state={sender.PlaybackSession.PlaybackState}");
        }
        catch
        {
            // ignore logging failures
        }

        if (_wantAutoPlay)
        {
            SafePlay("media-opened");
        }

        EmitStatus();
    }

    private void OnPlayerMediaFailed(MediaPlayer sender, MediaPlayerFailedEventArgs args)
    {
        if (!ReferenceEquals(sender, _player) || _failureReported)
        {
            return;
        }

        _failureReported = true;
        _wantAutoPlay = false;
        ReportFailure(_currentPath, $"{args.Error}: {args.ErrorMessage}");

        // Сразу убираем битый native-слой — WebView fallback покажет медиа.
        CollapseVideoElement();
        ReleasePlayer(disposeIfMatches: sender);
    }

    private void ReportFailure(string? path, string details)
    {
        var fileName = string.IsNullOrWhiteSpace(path) ? "?" : Path.GetFileName(path);
        var message = $"NativeForegroundMediaHost: {fileName} — {details}";
        ChyguiSlide.Data.InteractionLogger.Log(message);
        try
        {
            PlaybackFailed?.Invoke(this, message);
        }
        catch (Exception ex)
        {
            ChyguiSlide.Data.InteractionLogger.Log(
                $"NativeForegroundMediaHost: PlaybackFailed handler error: {ex.Message}");
        }
    }

    private void StartStatusTimer()
    {
        if (!_emitStatus || _statusTimer is not null)
        {
            return;
        }

        var queue = _video?.DispatcherQueue
                    ?? App.MainDispatcherQueue
                    ?? DispatcherQueue.GetForCurrentThread();
        if (queue is null)
        {
            return;
        }

        _statusTimer = queue.CreateTimer();
        _statusTimer.Interval = TimeSpan.FromMilliseconds(250);
        _statusTimer.IsRepeating = true;
        _statusTimer.Tick += OnStatusTimerTick;
        _statusTimer.Start();
    }

    private void StopStatusTimer()
    {
        if (_statusTimer is null)
        {
            return;
        }

        _statusTimer.Tick -= OnStatusTimerTick;
        _statusTimer.Stop();
        _statusTimer = null;
    }

    private void OnStatusTimerTick(DispatcherQueueTimer sender, object args)
        => EmitStatus();

    private void EmitStatus()
    {
        if (!_emitStatus || _player is null || !IsVideoActive || _failureReported)
        {
            return;
        }

        try
        {
            var session = _player.PlaybackSession;
            var position = session.Position.TotalSeconds;
            var duration = _player.NaturalDuration.TotalSeconds;
            if (double.IsNaN(duration) || duration < 0)
            {
                duration = 0;
            }

            var paused = session.PlaybackState != MediaPlaybackState.Playing;
            StatusChanged?.Invoke(this, new MediaPlaybackStatus(position, duration, paused));
        }
        catch
        {
            // ignore transient COM failures while player is tearing down
        }
    }

    private Task EnqueueAsync(Action action)
    {
        var dispatcher = _video?.DispatcherQueue
                         ?? App.MainDispatcherQueue
                         ?? DispatcherQueue.GetForCurrentThread();
        if (dispatcher is null)
        {
            action();
            return Task.CompletedTask;
        }

        var tcs = new TaskCompletionSource();
        if (!dispatcher.TryEnqueue(() =>
            {
                try
                {
                    action();
                    tcs.SetResult();
                }
                catch (Exception ex)
                {
                    tcs.SetException(ex);
                }
            }))
        {
            tcs.SetException(new InvalidOperationException("Dispatcher enqueue failed."));
        }

        return tcs.Task;
    }
}
