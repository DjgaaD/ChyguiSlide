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
    }

    public void BindViewModel(ProjectionDisplayViewModel viewModel, bool enableTransitionPlayer = true)
    {
        ViewModel = viewModel;
        DataContext = viewModel;
        ProjectionRoot.DataContext = viewModel;

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
    }

    private void OnProjectionRootSizeChanged(object sender, SizeChangedEventArgs e)
    {
        ViewModel?.UpdateWindowSize(e.NewSize.Width, e.NewSize.Height);
    }
}
