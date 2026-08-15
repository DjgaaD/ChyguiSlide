using ChyguiSlide.ViewModels;
using ChyguiSlide.Windows;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace ChyguiSlide.Controls;

/// <summary>
/// Единственная сцена проекции: и окно трансляции, и превью оператора
/// используют один и тот же экземпляр (пересадка в дерево + зеркало кадра).
/// </summary>
public sealed partial class ProjectionStageView : UserControl
{
    private MediaPlayerElement? _currentVideoPlayer;
    private ProjectionTransitionPlayer? _transitionPlayer;

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

    public void BindViewModel(ProjectionDisplayViewModel viewModel, bool enableTransitionPlayer = true)
    {
        ViewModel = viewModel;
        DataContext = viewModel;
        ProjectionRoot.DataContext = viewModel;

        ChyguiSlide.Data.InteractionLogger.Log("ProjectionStageView BindViewModel: enableTransitionPlayer=" + enableTransitionPlayer);

        if (enableTransitionPlayer)
        {
            _transitionPlayer ??= new ProjectionTransitionPlayer(
                IncomingSlideLayer,
                OutgoingSlideLayer);

            viewModel.SetTransitionPlayer(
                (mode, apply) =>
                    _transitionPlayer.PlayAsync(mode, apply, viewModel.SectionTransitionDurationMs),
                () => _transitionPlayer.ResetVisualState());
        }

        ProjectionRoot.SizeChanged -= OnProjectionRootSizeChanged;
        ProjectionRoot.SizeChanged += OnProjectionRootSizeChanged;
    }

    public void UnbindTransitionPlayer()
    {
        ViewModel?.SetTransitionPlayer(null);
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
