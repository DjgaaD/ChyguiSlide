using System;
using System.Threading;
using ChyguiSlide.Services.Models;
using ChyguiSlide.ViewModels;
using ChyguiSlide.Windows;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.Web.WebView2.Core;

namespace ChyguiSlide.Controls;

/// <summary>
/// Превью слайда тем же HTML/CSS, что и окно проектора.
/// </summary>
public sealed partial class WebProjectionPreview : UserControl
{
    private static int _profileCounter;

    private readonly string _profileName;
    private readonly NativeForegroundMediaHost _foregroundMedia = new();
    private ProjectionDisplayViewModel? _viewModel;
    private WebProjectionAdapter? _adapter;
    private bool _navigationHooked;
    private bool _initStarted;

    public WebProjectionPreview()
    {
        InitializeComponent();
        _profileName = "preview-" + Interlocked.Increment(ref _profileCounter);
        _foregroundMedia.Attach(ForegroundMediaPlayer, ForegroundMediaImage, emitPlaybackStatus: true);
        _foregroundMedia.StatusChanged += OnForegroundMediaStatusChanged;
        Loaded += OnLoaded;
        SizeChanged += OnPreviewSizeChanged;
        Unloaded += OnUnloaded;
    }

    public MediaPlayerElement? VideoPlayerElement => VideoPlayer;

    public Image? NdiVideoImageElement => NdiVideoImage;

    public MediaPlayerElement? BackgroundVideoPlayerElement => null;

    public NativeForegroundMediaHost ForegroundMedia => _foregroundMedia;

    public WebProjectionAdapter? Adapter => _adapter;

    private int _outputWidth = 1920;
    private int _outputHeight = 1080;

    private bool _instantSlides;

    public void BindViewModel(ProjectionDisplayViewModel viewModel)
    {
        _viewModel = viewModel ?? throw new ArgumentNullException(nameof(viewModel));
        DataContext = viewModel;
        ApplyOutputSize(_outputWidth, _outputHeight);
        _ = EnsureInitializedAsync();
    }

    public void SetInstantSlides(bool enabled)
    {
        _instantSlides = enabled;
        _adapter?.SetInstantSlides(enabled);
    }

    public void ApplyOutputSize(int width, int height)
    {
        _outputWidth = Math.Max(800, width);
        _outputHeight = Math.Max(600, height);
        _viewModel?.UpdateWindowSize(_outputWidth, _outputHeight);
    }

    public void SyncNow()
    {
        _adapter?.PushFullState();
    }

    public void MediaPlay() => _foregroundMedia.Play();

    public void MediaPause() => _foregroundMedia.Pause();

    public void MediaSeek(double positionSec) => _foregroundMedia.Seek(positionSec);

    public void MediaSetLoop(bool loop) => _foregroundMedia.SetLoop(loop);

    public void ShowWebPlaylistMedia(string path, bool isVideo, bool loop)
        => _adapter?.ShowPlaylistMedia(path, isVideo, loop);

    public void ShowWebMediaCover()
        => _adapter?.ShowMediaCover();

    public void HideWebPlaylistMedia()
        => _adapter?.HidePlaylistMedia();

    public event EventHandler<MediaPlaybackStatus>? ForegroundMediaStatusChanged;

    private void OnForegroundMediaStatusChanged(object? sender, MediaPlaybackStatus e)
        => ForegroundMediaStatusChanged?.Invoke(this, e);

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        _ = EnsureInitializedAsync();
    }

    private void OnUnloaded(object sender, RoutedEventArgs e)
    {
        _foregroundMedia.Hide();
    }

    private void OnPreviewSizeChanged(object sender, SizeChangedEventArgs e)
    {
        if (e.NewSize.Width < 2 || e.NewSize.Height < 2)
        {
            return;
        }

        _adapter?.RefreshPreviewFit();
    }

    private async System.Threading.Tasks.Task EnsureInitializedAsync()
    {
        if (_initStarted || _viewModel is null)
        {
            return;
        }

        _initStarted = true;
        try
        {
            if (!_navigationHooked)
            {
                _navigationHooked = true;
                WebView.NavigationCompleted += OnNavigationCompleted;
            }

            await WebProjectionRuntime.PrepareWebViewAsync(WebView, _profileName);
        }
        catch (Exception ex)
        {
            ChyguiSlide.Data.InteractionLogger.Log($"[WebProjectionPreview] init failed: {ex.Message}");
            _initStarted = false;
        }
    }

    private void OnNavigationCompleted(object? sender, CoreWebView2NavigationCompletedEventArgs e)
    {
        if (!e.IsSuccess)
        {
            ChyguiSlide.Data.InteractionLogger.Log(
                $"[WebProjectionPreview] Navigation failed: {e.WebErrorStatus}");
            return;
        }

        if (WebView.CoreWebView2 is null || _viewModel is null)
        {
            return;
        }

        if (_adapter is null)
        {
            _adapter = new WebProjectionAdapter(
                WebView.CoreWebView2,
                _viewModel,
                previewFit: false,
                instantSlides: _instantSlides,
                emitMediaTransportClock: false);
        }

        _adapter.MarkPageReady();
    }
}
