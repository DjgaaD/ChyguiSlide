using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using ChyguiSlide.Services.Models;
using ChyguiSlide.ViewModels;
using Microsoft.UI.Dispatching;
using Microsoft.Web.WebView2.Core;
using Windows.UI;

namespace ChyguiSlide.Windows;

/// <summary>
/// Передаёт состояние ProjectionDisplayViewModel в WebView2.
/// Сообщения ставятся в очередь, пока страница не готова; слайды debounce’ятся.
/// </summary>
public sealed class WebProjectionAdapter : IDisposable
{
    private readonly CoreWebView2 _webView;
    private readonly ProjectionDisplayViewModel _viewModel;
    private readonly DispatcherQueue _dispatcher;
    private readonly object _gate = new();
    private readonly List<string> _pendingJson = new();

    private bool _disposed;
    private bool _pageReady;
    private bool _slideDebounceQueued;

    private readonly bool _previewFit;
    private readonly bool _emitMediaTransportClock;
    private bool _instantSlides;
    private string? _lastForegroundMediaPath;
    private string? _lastForegroundMediaUrl;
    private bool _webForegroundActive;

    public event EventHandler<MediaPlaybackStatus>? MediaStatusChanged;

    public bool IsWebForegroundActive => _webForegroundActive;

    public WebProjectionAdapter(
        CoreWebView2 webView,
        ProjectionDisplayViewModel viewModel,
        bool previewFit = false,
        bool instantSlides = false,
        bool emitMediaTransportClock = true)
    {
        _webView = webView ?? throw new ArgumentNullException(nameof(webView));
        _viewModel = viewModel ?? throw new ArgumentNullException(nameof(viewModel));
        _previewFit = previewFit;
        _instantSlides = instantSlides;
        _emitMediaTransportClock = emitMediaTransportClock;
        _dispatcher = DispatcherQueue.GetForCurrentThread()
            ?? App.MainDispatcherQueue
            ?? throw new InvalidOperationException("DispatcherQueue недоступен для WebProjectionAdapter.");

        ChyguiSlide.Data.InteractionLogger.Log("[WebProjectionAdapter] Constructor called");
        System.Diagnostics.Debug.WriteLine("[WebProjectionAdapter] Constructor called");

        _webView.WebMessageReceived += OnWebMessageReceived;
        SubscribeToViewModel();
    }

    /// <summary>Вызвать после NavigationCompleted — тогда уходит initial state и очередь.</summary>
    public void MarkPageReady()
    {
        if (_disposed)
        {
            return;
        }

        lock (_gate)
        {
            _pageReady = true;
        }

        ChyguiSlide.Data.InteractionLogger.Log("[WebProjectionAdapter] Page ready — flushing state");
        SendInitialState();
        FlushPending();
    }

    private void SubscribeToViewModel()
    {
        _viewModel.PropertyChanged += OnPropertyChanged;
        _viewModel.Lines.CollectionChanged += OnLinesChanged;
        _viewModel.BackgroundMediaChanged += OnBackgroundMediaChanged;
        _viewModel.ForegroundMediaChanged += OnForegroundMediaChanged;
    }

    private void UnsubscribeFromViewModel()
    {
        _viewModel.PropertyChanged -= OnPropertyChanged;
        _viewModel.Lines.CollectionChanged -= OnLinesChanged;
        _viewModel.BackgroundMediaChanged -= OnBackgroundMediaChanged;
        _viewModel.ForegroundMediaChanged -= OnForegroundMediaChanged;
        _webView.WebMessageReceived -= OnWebMessageReceived;
    }

    private void OnBackgroundMediaChanged(object? sender, EventArgs e)
    {
        if (!_disposed)
        {
            _ = SendBackgroundUpdateAsync();
        }
    }

    private void OnForegroundMediaChanged(object? sender, EventArgs e)
    {
        if (!_disposed)
        {
            _ = SendForegroundMediaUpdateAsync();
        }
    }

    private void OnWebMessageReceived(object? sender, CoreWebView2WebMessageReceivedEventArgs e)
    {
        try
        {
            var json = e.WebMessageAsJson;
            // mediaStatus тикает часто — не пишем на диск.
            if (json.Contains("mediaStatus", StringComparison.Ordinal))
            {
                // fall through to parse without logging
            }
            else
            {
                ChyguiSlide.Data.InteractionLogger.LogVerbose($"[JS] {json}");
            }

            // chrome.webview.postMessage(string) приходит как JSON-строка в кавычках
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            if (root.ValueKind == JsonValueKind.String)
            {
                var inner = root.GetString();
                if (string.IsNullOrWhiteSpace(inner))
                {
                    return;
                }

                using var innerDoc = JsonDocument.Parse(inner);
                TryHandleMediaStatus(innerDoc.RootElement);
                return;
            }

            TryHandleMediaStatus(root);
        }
        catch (Exception ex)
        {
            ChyguiSlide.Data.InteractionLogger.LogError(
                $"[WebProjectionAdapter] Failed to handle JS message: {ex.Message}");
        }
    }

    private void TryHandleMediaStatus(JsonElement root)
    {
        if (!root.TryGetProperty("type", out var typeProp)
            || !string.Equals(typeProp.GetString(), "mediaStatus", StringComparison.Ordinal))
        {
            return;
        }

        var position = root.TryGetProperty("position", out var posProp) && posProp.TryGetDouble(out var p) ? p : 0;
        var duration = root.TryGetProperty("duration", out var durProp) && durProp.TryGetDouble(out var d) ? d : 0;
        var paused = root.TryGetProperty("paused", out var pausedProp) && pausedProp.ValueKind is JsonValueKind.True;

        var status = new MediaPlaybackStatus(position, duration, paused);
        _dispatcher.TryEnqueue(() =>
        {
            MediaStatusChanged?.Invoke(this, status);
            // Только один «часы» для UI: иначе превью и окно дёргают ползунок туда-сюда.
            if (_emitMediaTransportClock)
            {
                _viewModel.ReportMediaStatus(status);
            }
        });
    }

    private void OnPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (_disposed)
        {
            return;
        }

        switch (e.PropertyName)
        {
            case nameof(ProjectionDisplayViewModel.ProjectionMarginLeft):
            case nameof(ProjectionDisplayViewModel.ProjectionMarginRight):
            case nameof(ProjectionDisplayViewModel.ProjectionMarginTop):
            case nameof(ProjectionDisplayViewModel.ProjectionMarginBottom):
                SendProjectionMargins();
                QueueSlideUpdate();
                break;
            case nameof(ProjectionDisplayViewModel.ReferenceCaption):
                QueueSlideUpdate();
                break;
            case nameof(ProjectionDisplayViewModel.ShowBibleReference):
            case nameof(ProjectionDisplayViewModel.BibleReferencePlacement):
            case nameof(ProjectionDisplayViewModel.BibleReferenceAlignment):
                QueueSlideUpdate();
                break;
            case nameof(ProjectionDisplayViewModel.TransitionStyle):
                SendTransitionStyle();
                break;
            case nameof(ProjectionDisplayViewModel.SectionTransitionDurationMs):
                SendTransitionDuration();
                break;
            case nameof(ProjectionDisplayViewModel.PrimaryBrush):
            case nameof(ProjectionDisplayViewModel.FontFamilyName):
            case nameof(ProjectionDisplayViewModel.TextOutlineBrush):
            case nameof(ProjectionDisplayViewModel.TextOutlineThickness):
            case nameof(ProjectionDisplayViewModel.TextOutlineOpacity):
                // Стили текста — только для слайдов песен/Библии.
                if (!IsPlaylistMediaMode)
                {
                    SendThemeUpdate();
                }

                break;
            case nameof(ProjectionDisplayViewModel.BackgroundBrush):
            case nameof(ProjectionDisplayViewModel.BackgroundImageSource):
            case nameof(ProjectionDisplayViewModel.IsBackgroundVideoVisible):
            case nameof(ProjectionDisplayViewModel.IsBackgroundImageVisible):
            case nameof(ProjectionDisplayViewModel.LoopBackgroundMedia):
                _ = SendBackgroundUpdateAsync();
                break;
            case nameof(ProjectionDisplayViewModel.MediaPath):
            case nameof(ProjectionDisplayViewModel.ContentKind):
                _ = OnPlaylistMediaModeChangedAsync();
                break;
            case nameof(ProjectionDisplayViewModel.MediaLoopEnabled):
                break;
        }
    }

    private void OnLinesChanged(object? sender, System.Collections.Specialized.NotifyCollectionChangedEventArgs e)
        => QueueSlideUpdate();

    private void QueueSlideUpdate()
    {
        if (_disposed || _slideDebounceQueued)
        {
            return;
        }

        _slideDebounceQueued = true;
        _ = _dispatcher.TryEnqueue(DispatcherQueuePriority.Low, () =>
        {
            _slideDebounceQueued = false;
            if (!_disposed)
            {
                SendSlideUpdate();
            }
        });
    }

    private void SendInitialState()
    {
        _ = SendBackgroundUpdateAsync();
        SendThemeUpdate();
        SendProjectionMargins();
        SendTransitionStyle();
        SendTransitionDuration();
        if (_previewFit)
        {
            SendMessage(new { type = "setPreviewFit", enabled = true });
        }

        SendSlideUpdate();
        _ = SendForegroundMediaUpdateAsync(force: true);
    }

    public void PushFullState() => SendInitialState();

    public void RefreshPreviewFit()
    {
        if (_previewFit)
        {
            SendMessage(new { type = "setPreviewFit", enabled = true });
        }
    }

    public void SetInstantSlides(bool enabled)
    {
        if (_instantSlides == enabled)
        {
            return;
        }

        _instantSlides = enabled;
        SendTransitionStyle();
    }

    public void MediaPlay() => SendMediaCommand("play");

    public void MediaPause() => SendMediaCommand("pause");

    public void MediaSeek(double positionSec) => SendMediaCommand("seek", positionSec);

        public void MediaSetLoop(bool loop)
    {
        _mediaLoop = loop;
        _viewModel.MediaLoopEnabled = loop;
        SendMessage(new { type = "mediaCommand", action = "setLoop", loopEnabled = loop });
    }

    private bool _mediaLoop;

    private void SendMediaCommand(string action, double? positionSec = null)
    {
        if (positionSec is null)
        {
            SendMessage(new { type = "mediaCommand", action });
        }
        else
        {
            SendMessage(new { type = "mediaCommand", action, positionSec = positionSec.Value });
        }
    }

    private bool IsPlaylistMediaMode
        => _viewModel.ContentKind == ProjectionContentKind.Media
           && !string.IsNullOrWhiteSpace(_viewModel.MediaPath);

    private async Task OnPlaylistMediaModeChangedAsync()
    {
        if (_disposed)
        {
            return;
        }

        if (IsPlaylistMediaMode)
        {
            // Медиа: выключаем стили/фон темы. showMedia придёт из ProjectionDisplayService.
            await SendBackgroundUpdateAsync().ConfigureAwait(true);
            return;
        }

        // Слайды песен/Библии: убираем слой медиа и возвращаем тему.
        if (_webForegroundActive)
        {
            HidePlaylistMedia();
        }
        else
        {
            SendMessage(new { type = "hideMedia" });
        }

        await SendBackgroundUpdateAsync().ConfigureAwait(true);
        SendThemeUpdate();
        SendSlideUpdate();
    }

    private void SendSlideUpdate()
    {
        // Пока на экране foreground media — не шлём текстовый слайд
        // (иначе JS скроет медиа при lines>0; showMedia сам чистит текст).
        if (IsPlaylistMediaMode)
        {
            return;
        }

        var lines = _viewModel.Lines.Select(l => l.Text).ToList();
        SendMessage(new
        {
            type = "updateSlide",
            lines,
            referenceCaption = _viewModel.ReferenceCaption,
            showBibleReference = _viewModel.ShowBibleReference,
            bibleReferencePlacement = _viewModel.BibleReferencePlacement.ToString(),
            bibleReferenceAlignment = _viewModel.BibleReferenceAlignment,
            referenceFontSize = _viewModel.ReferenceFontSize,
            primaryColor = GetCssColorFromBrush(_viewModel.PrimaryBrush),
            fontSize = _viewModel.DisplayFontSize,
            fontFamily = _viewModel.FontFamilyName,
            fontWeight = (int)_viewModel.FontWeight.Weight,
            textAlignment = _viewModel.TextAlignment,
            lineSpacing = _viewModel.LineSpacing > 0 ? _viewModel.LineSpacing : 12,
            textOutlineColor = GetCssColorFromBrush(_viewModel.TextOutlineBrush),
            textOutlineThickness = _viewModel.TextOutlineThickness,
            textOutlineOpacity = _viewModel.TextOutlineOpacity
        });
    }

    private void SendTransitionStyle()
    {
        SendMessage(new
        {
            type = "setTransitionStyle",
            style = _instantSlides ? "None" : _viewModel.TransitionStyle.ToString()
        });
    }

    private int _backgroundUpdateVersion;

    private async Task SendBackgroundUpdateAsync()
    {
        if (_disposed)
        {
            return;
        }

        var version = Interlocked.Increment(ref _backgroundUpdateVersion);

        // Плейлист-медиа: стили темы не применяются — только чёрный холст под файлом.
        if (IsPlaylistMediaMode)
        {
            if (version != _backgroundUpdateVersion || _disposed)
            {
                return;
            }

            SendMessage(new
            {
                type = "updateBackground",
                color = "#000000",
                imageUrl = (string?)null,
                videoUrl = (string?)null,
                loop = false
            });
            ChyguiSlide.Data.InteractionLogger.Log(
                "[WebProjectionAdapter] Background cleared for playlist media (no theme)");
            return;
        }

        string? videoUrl = null;
        string? imageUrl = null;

        if (_viewModel.IsBackgroundVideoVisible
            && !string.IsNullOrWhiteSpace(_viewModel.BackgroundVideoPath))
        {
            var playablePath = _viewModel.BackgroundVideoPath;

            if (version != _backgroundUpdateVersion || _disposed)
            {
                return;
            }

            videoUrl = ToBackgroundMediaUrl(playablePath);
        }
        else if (_viewModel.IsBackgroundImageVisible)
        {
            imageUrl = ToBackgroundMediaUrl(TryGetLocalPath(_viewModel.BackgroundImageSource))
                ?? GetImageUrl(_viewModel.BackgroundImageSource);
        }

        if (version != _backgroundUpdateVersion || _disposed)
        {
            return;
        }

        SendMessage(new
        {
            type = "updateBackground",
            color = GetCssColorFromBrush(_viewModel.BackgroundBrush),
            imageUrl,
            videoUrl,
            loop = _viewModel.LoopBackgroundMedia
        });

        if (!string.IsNullOrEmpty(videoUrl))
        {
            ChyguiSlide.Data.InteractionLogger.Log($"[WebProjectionAdapter] Background video: {videoUrl}");
        }

        await Task.CompletedTask;
    }

    private Task SendForegroundMediaUpdateAsync(bool force = false)
    {
        if (_disposed)
        {
            return Task.CompletedTask;
        }

        if (IsPlaylistMediaMode || _webForegroundActive)
        {
            return Task.CompletedTask;
        }

        _lastForegroundMediaPath = null;
        SendMessage(new { type = "hideMedia" });
        return Task.CompletedTask;
    }

    /// <summary>
    /// Fallback / primary WebView playback for playlist media.
    /// </summary>
    public void ShowPlaylistMedia(string path, bool isVideo, bool loop)
    {
        if (_disposed || string.IsNullOrWhiteSpace(path))
        {
            return;
        }

        // Сразу гасим фон темы, даже если URL ещё не готов.
        SendMessage(new
        {
            type = "updateBackground",
            color = "#000000",
            imageUrl = (string?)null,
            videoUrl = (string?)null,
            loop = false
        });

        var url = PlaylistMediaWebHost.MapAndGetUrl(_webView, path);
        if (string.IsNullOrWhiteSpace(url))
        {
            ChyguiSlide.Data.InteractionLogger.Log(
                $"[WebProjectionAdapter] ShowPlaylistMedia: no URL for {Path.GetFileName(path)}");
            ShowMediaCover();
            return;
        }

        // Уже показываем тот же файл — не перезагружаем (иначе ползунок/позиция сбрасываются).
        if (_webForegroundActive
            && string.Equals(_lastForegroundMediaPath, path, StringComparison.OrdinalIgnoreCase)
            && string.Equals(_lastForegroundMediaUrl, url, StringComparison.OrdinalIgnoreCase)
            && _pageReady)
        {
            _mediaLoop = loop;
            _viewModel.MediaLoopEnabled = loop;
            SendMessage(new { type = "mediaCommand", action = "setLoop", loopEnabled = loop });
            return;
        }

        _webForegroundActive = true;
        _lastForegroundMediaPath = path;
        _lastForegroundMediaUrl = url;
        _mediaLoop = loop;
        _viewModel.MediaLoopEnabled = loop;
        SendMessage(new
        {
            type = "showMedia",
            mediaUrl = url,
            isVideo,
            loopEnabled = loop
        });
        ChyguiSlide.Data.InteractionLogger.Log(
            $"[WebProjectionAdapter] Web playlist showMedia: {url} ← {path}");
    }

    /// <summary>Чёрный слой поверх фона темы (когда файл плейлиста нельзя открыть в WebView).</summary>
    public void ShowMediaCover()
    {
        if (_disposed)
        {
            return;
        }

        _webForegroundActive = true;
        _lastForegroundMediaPath = null;
        _lastForegroundMediaUrl = null;
        SendMessage(new { type = "showMediaCover" });
    }

    public void HidePlaylistMedia()
    {
        if (_disposed)
        {
            return;
        }

        _webForegroundActive = false;
        _lastForegroundMediaPath = null;
        _lastForegroundMediaUrl = null;
        try
        {
            SendMessage(new { type = "hideMedia" });
        }
        catch (Exception ex)
        {
            ChyguiSlide.Data.InteractionLogger.Log(
                $"[WebProjectionAdapter] HidePlaylistMedia: {ex.Message}");
        }
    }

    /// <summary>
    /// Файлы из %LocalAppData%\ChyguiSlide\Backgrounds отдаём через virtual host
    /// (NavigateToString иначе блокирует file:// для video).
    /// </summary>
    private static string? ToBackgroundMediaUrl(string? path)
        => ToVirtualHostMediaUrl(path, "Backgrounds", "chygui.backgrounds");

    private static string? ToVirtualHostMediaUrl(string? path, string folderName, string hostName)
    {
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
        {
            return null;
        }

        try
        {
            var full = Path.GetFullPath(path);
            var root = Path.GetFullPath(Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    "ChyguiSlide",
                    folderName))
                .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
                + Path.DirectorySeparatorChar;

            if (full.StartsWith(root, StringComparison.OrdinalIgnoreCase))
            {
                var relative = full[root.Length..].Replace('\\', '/');
                var encoded = string.Join('/',
                    relative.Split('/', StringSplitOptions.RemoveEmptyEntries)
                        .Select(Uri.EscapeDataString));
                return $"https://{hostName}/" + encoded;
            }

            return new Uri(full).AbsoluteUri;
        }
        catch
        {
            return null;
        }
    }

    private static string? TryGetLocalPath(Microsoft.UI.Xaml.Media.ImageSource? imageSource)
    {
        if (imageSource is Microsoft.UI.Xaml.Media.Imaging.BitmapImage { UriSource: not null } bitmap)
        {
            try
            {
                if (bitmap.UriSource.IsFile)
                {
                    return bitmap.UriSource.LocalPath;
                }
            }
            catch
            {
                // ignore
            }
        }

        return null;
    }

    private void SendProjectionMargins()
    {
        SendMessage(new
        {
            type = "setProjectionMargins",
            marginLeft = _viewModel.ProjectionMarginLeft,
            marginRight = _viewModel.ProjectionMarginRight,
            marginTop = _viewModel.ProjectionMarginTop,
            marginBottom = _viewModel.ProjectionMarginBottom
        });
    }

    private void SendThemeUpdate()
    {
        if (IsPlaylistMediaMode)
        {
            return;
        }

        SendMessage(new
        {
            type = "updateTheme",
            primaryColor = GetCssColorFromBrush(_viewModel.PrimaryBrush),
            fontSize = _viewModel.DisplayFontSize,
            fontFamily = _viewModel.FontFamilyName,
            fontWeight = (int)_viewModel.FontWeight.Weight,
            textAlignment = _viewModel.TextAlignment,
            lineSpacing = _viewModel.LineSpacing > 0 ? _viewModel.LineSpacing : 12,
            textOutlineColor = GetCssColorFromBrush(_viewModel.TextOutlineBrush),
            textOutlineThickness = _viewModel.TextOutlineThickness,
            textOutlineOpacity = _viewModel.TextOutlineOpacity
        });
    }

    private void SendTransitionDuration()
    {
        var ms = _viewModel.SectionTransitionDurationMs;
        if (ms <= 0)
        {
            ms = 750;
        }

        SendMessage(new
        {
            type = "setTransitionDuration",
            durationMs = ms
        });
    }

    private void SendMessage(object message)
    {
        if (_disposed)
        {
            return;
        }

        string json;
        try
        {
            json = JsonSerializer.Serialize(message);
        }
        catch (Exception ex)
        {
            ChyguiSlide.Data.InteractionLogger.Log($"[WebProjectionAdapter] Serialize failed: {ex.Message}");
            return;
        }

        lock (_gate)
        {
            if (!_pageReady)
            {
                var type = TryGetMessageType(json);
                if (type is not null)
                {
                    _pendingJson.RemoveAll(j => TryGetMessageType(j) == type);
                }

                _pendingJson.Add(json);
                return;
            }
        }

        PostJson(json);
    }

    private void FlushPending()
    {
        List<string> pending;
        lock (_gate)
        {
            pending = _pendingJson.ToList();
            _pendingJson.Clear();
        }

        foreach (var json in pending)
        {
            PostJson(json);
        }
    }

    private void PostJson(string json)
    {
        if (_disposed)
        {
            return;
        }

        try
        {
            var type = TryGetMessageType(json);
            if (!string.Equals(type, "mediaCommand", StringComparison.OrdinalIgnoreCase)
                || !json.Contains("\"action\":\"seek\"", StringComparison.Ordinal))
            {
                ChyguiSlide.Data.InteractionLogger.LogVerbose(
                    $"[WebProjectionAdapter] Sending message: {json}");
            }

            _webView.PostWebMessageAsJson(json);
        }
        catch (Exception ex)
        {
            ChyguiSlide.Data.InteractionLogger.LogError(
                $"[WebProjectionAdapter] Failed to send message: {ex.Message}");
        }
    }

    private static string? TryGetMessageType(string json)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            if (doc.RootElement.TryGetProperty("type", out var typeProp))
            {
                return typeProp.GetString();
            }
        }
        catch
        {
            // ignore
        }

        return null;
    }

    private static string? GetCssColorFromBrush(Microsoft.UI.Xaml.Media.Brush? brush)
    {
        if (brush is Microsoft.UI.Xaml.Media.SolidColorBrush solid)
        {
            return ToCssColor(solid.Color);
        }

        return null;
    }

    private static string ToCssColor(Color color)
        => $"#{color.R:X2}{color.G:X2}{color.B:X2}";

    private static string? GetImageUrl(Microsoft.UI.Xaml.Media.ImageSource? imageSource)
    {
        if (imageSource is Microsoft.UI.Xaml.Media.Imaging.BitmapImage { UriSource: not null } bitmap)
        {
            return bitmap.UriSource.AbsoluteUri;
        }

        return null;
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        lock (_gate)
        {
            _pageReady = false;
            _pendingJson.Clear();
        }

        try
        {
            UnsubscribeFromViewModel();
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[WebProjectionAdapter] Dispose: {ex.Message}");
        }

        ChyguiSlide.Data.InteractionLogger.Log("[WebProjectionAdapter] Disposed");
    }
}
