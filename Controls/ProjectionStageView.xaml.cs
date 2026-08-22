using ChyguiSlide.ViewModels;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace ChyguiSlide.Controls;

/// <summary>
/// Сцена превью программы в Live Control (зеркало текущего слайда).
/// Вывод на экран проекции — только WebView2 (<see cref="ChyguiSlide.Windows.ProjectionWindowWeb"/>).
/// </summary>
public sealed partial class ProjectionStageView : UserControl
{
    private MediaPlayerElement? _currentVideoPlayer;

    public ProjectionDisplayViewModel ViewModel { get; private set; } = null!;

    public MediaPlayerElement? VideoPlayerElement => _currentVideoPlayer ?? VideoPlayer;

    public MediaPlayerElement BackgroundVideoPlayerElement => BackgroundVideoPlayer;

    public Image? NdiVideoImageElement => NdiVideoImage;

    public Grid? ContentHostGrid => ContentHost;

    public Grid? ProjectionRootGrid => ProjectionRoot;

    public ProjectionStageView()
    {
        InitializeComponent();
        ChyguiSlide.Data.InteractionLogger.Log("ProjectionStageView ctor");
        Loaded += ProjectionStageView_Loaded;
        Unloaded += ProjectionStageView_Unloaded;
    }

    public void BindViewModel(ProjectionDisplayViewModel viewModel)
    {
        ViewModel = viewModel;
        DataContext = viewModel;
        ProjectionRoot.DataContext = viewModel;

        ChyguiSlide.Data.InteractionLogger.Log("ProjectionStageView BindViewModel");

        ProjectionRoot.SizeChanged -= OnProjectionRootSizeChanged;
        ProjectionRoot.SizeChanged += OnProjectionRootSizeChanged;
    }

    public void SetVideoPlayer(MediaPlayerElement? videoPlayer)
    {
        _currentVideoPlayer = videoPlayer;
        try
        {
            var hasMediaPlayer = videoPlayer?.MediaPlayer is not null;
            ChyguiSlide.Data.InteractionLogger.Log($"ProjectionStageView SetVideoPlayer: videoPlayer={(videoPlayer is null ? "null" : videoPlayer.GetHashCode().ToString())}, hasMediaPlayer={hasMediaPlayer}");
        }
        catch
        {
            // ignore logging errors
        }
    }

    private void ProjectionStageView_Loaded(object? sender, RoutedEventArgs e)
    {
        try
        {
            ChyguiSlide.Data.InteractionLogger.Log($"ProjectionStageView Loaded: VideoPlayer={(VideoPlayer is null ? "null" : VideoPlayer.GetHashCode().ToString())}, BackgroundVideoPlayer={(BackgroundVideoPlayer is null ? "null" : BackgroundVideoPlayer.GetHashCode().ToString())}, NdiImage={(NdiVideoImage is null ? "null" : NdiVideoImage.GetHashCode().ToString())}");
            var vpState = VideoPlayer?.MediaPlayer?.CurrentState.ToString() ?? "no-media";
            var bgState = BackgroundVideoPlayer?.MediaPlayer?.CurrentState.ToString() ?? "no-media";
            ChyguiSlide.Data.InteractionLogger.Log($"ProjectionStageView Loaded states: VideoPlayerState={vpState}, BackgroundState={bgState}");
        }
        catch
        {
        }
    }

    private void ProjectionStageView_Unloaded(object? sender, RoutedEventArgs e)
    {
        try
        {
            ChyguiSlide.Data.InteractionLogger.Log("ProjectionStageView Unloaded");
        }
        catch
        {
        }
    }

    private void OnProjectionRootSizeChanged(object sender, SizeChangedEventArgs e)
    {
        ViewModel?.UpdateWindowSize(e.NewSize.Width, e.NewSize.Height);
    }
}
