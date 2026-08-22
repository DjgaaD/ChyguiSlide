using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
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

    public WebProjectionAdapter(CoreWebView2 webView, ProjectionDisplayViewModel viewModel)
    {
        _webView = webView ?? throw new ArgumentNullException(nameof(webView));
        _viewModel = viewModel ?? throw new ArgumentNullException(nameof(viewModel));
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
    }

    private void UnsubscribeFromViewModel()
    {
        _viewModel.PropertyChanged -= OnPropertyChanged;
        _viewModel.Lines.CollectionChanged -= OnLinesChanged;
        _viewModel.BackgroundMediaChanged -= OnBackgroundMediaChanged;
        _webView.WebMessageReceived -= OnWebMessageReceived;
    }

    private void OnBackgroundMediaChanged(object? sender, EventArgs e)
    {
        if (!_disposed)
        {
            SendBackgroundUpdate();
        }
    }

    private void OnWebMessageReceived(object? sender, CoreWebView2WebMessageReceivedEventArgs e)
    {
        try
        {
            ChyguiSlide.Data.InteractionLogger.Log($"[JS] {e.WebMessageAsJson}");
        }
        catch (Exception ex)
        {
            ChyguiSlide.Data.InteractionLogger.Log($"[WebProjectionAdapter] Failed to handle JS message: {ex.Message}");
        }
    }

    private void OnPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (_disposed)
        {
            return;
        }

        switch (e.PropertyName)
        {
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
            // DisplayFontSize / LineSpacing / FontWeight / TextAlignment — только в updateSlide,
            // иначе до анимации «сжимают» уходящий текст (новый gap/кегль на старом слое).
            case nameof(ProjectionDisplayViewModel.PrimaryBrush):
            case nameof(ProjectionDisplayViewModel.FontFamilyName):
            case nameof(ProjectionDisplayViewModel.TextOutlineBrush):
            case nameof(ProjectionDisplayViewModel.TextOutlineThickness):
            case nameof(ProjectionDisplayViewModel.TextOutlineOpacity):
                SendThemeUpdate();
                break;
            case nameof(ProjectionDisplayViewModel.BackgroundBrush):
            case nameof(ProjectionDisplayViewModel.BackgroundImageSource):
            case nameof(ProjectionDisplayViewModel.IsBackgroundVideoVisible):
            case nameof(ProjectionDisplayViewModel.IsBackgroundImageVisible):
            case nameof(ProjectionDisplayViewModel.LoopBackgroundMedia):
                SendBackgroundUpdate();
                break;
        }
    }

    private void OnLinesChanged(object? sender, System.Collections.Specialized.NotifyCollectionChangedEventArgs e)
        => QueueSlideUpdate();

    /// <summary>
    /// Clear+Add по одной строке дают N событий — шлём один update после пакета.
    /// </summary>
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
        SendBackgroundUpdate();
        SendThemeUpdate();
        SendTransitionStyle();
        SendTransitionDuration();
        SendSlideUpdate();
    }

    private void SendSlideUpdate()
    {
        var lines = _viewModel.Lines.Select(l => l.Text).ToList();
        // Стиль НЕ кладём в payload — JS держит его из setTransitionStyle.
        // Тема (особенно fontSize) — вместе со слайдом, чтобы JS применил кегль
        // только в fillLayer при opacity:0 (как смена текста в демо Fade).
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
            style = _viewModel.TransitionStyle.ToString()
        });
    }

    private void SendBackgroundUpdate()
    {
        string? videoUrl = null;
        string? imageUrl = null;

        if (_viewModel.IsBackgroundVideoVisible
            && !string.IsNullOrWhiteSpace(_viewModel.BackgroundVideoPath))
        {
            videoUrl = ToBackgroundMediaUrl(_viewModel.BackgroundVideoPath);
        }
        else if (_viewModel.IsBackgroundImageVisible)
        {
            imageUrl = ToBackgroundMediaUrl(TryGetLocalPath(_viewModel.BackgroundImageSource))
                ?? GetImageUrl(_viewModel.BackgroundImageSource);
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
    }

    /// <summary>
    /// Файлы из %LocalAppData%\ChyguiSlide\Backgrounds отдаём через virtual host
    /// (NavigateToString иначе блокирует file:// для video).
    /// </summary>
    private static string? ToBackgroundMediaUrl(string? path)
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
                    "Backgrounds"))
                .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
                + Path.DirectorySeparatorChar;

            if (full.StartsWith(root, StringComparison.OrdinalIgnoreCase))
            {
                var relative = full[root.Length..].Replace('\\', '/');
                var encoded = string.Join('/',
                    relative.Split('/', StringSplitOptions.RemoveEmptyEntries)
                        .Select(Uri.EscapeDataString));
                return "https://chygui.backgrounds/" + encoded;
            }

            // Вне каталога Backgrounds — file:// (может не сработать для video при NavigateToString)
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

    private void SendThemeUpdate()
    {
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
                // Для однотипных апдейтов оставляем только последний
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
            ChyguiSlide.Data.InteractionLogger.Log($"[WebProjectionAdapter] Sending message: {json}");
            _webView.PostWebMessageAsJson(json);
        }
        catch (Exception ex)
        {
            // Не спамим стек на каждый Clear/Add — достаточно сообщения
            ChyguiSlide.Data.InteractionLogger.Log($"[WebProjectionAdapter] Failed to send message: {ex.Message}");
            System.Diagnostics.Debug.WriteLine($"[WebProjectionAdapter] Failed to send message: {ex.Message}");
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

    /// <summary>Windows Color.ToString() даёт #AARRGGBB — CSS это читает как RRGGBBAA.</summary>
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
