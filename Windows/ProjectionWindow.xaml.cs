using ChyguiSlide.ViewModels;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace ChyguiSlide.Windows;

public sealed partial class ProjectionWindow : Window
{
    public ProjectionDisplayViewModel ViewModel { get; }

    private MediaPlayerElement? _currentVideoPlayer;
    private readonly ProjectionTransitionPlayer _transitionPlayer;

    public MediaPlayerElement? VideoPlayerElement => _currentVideoPlayer ?? VideoPlayer;

    public MediaPlayerElement BackgroundVideoPlayerElement => BackgroundVideoPlayer;

    public Microsoft.UI.Xaml.Controls.Image? NdiVideoImageElement => NdiVideoImage;

    public Grid? ContentHostGrid => ContentHost;

    public Grid? ProjectionRootGrid => ProjectionRoot;

    public void SetVideoPlayer(MediaPlayerElement? videoPlayer)
    {
        _currentVideoPlayer = videoPlayer;
    }

    public ProjectionWindow(ProjectionDisplayViewModel viewModel)
    {
        InitializeComponent();
        ViewModel = viewModel;
        ProjectionRoot.DataContext = ViewModel;

        _transitionPlayer = new ProjectionTransitionPlayer(OutgoingSlideLayer, IncomingSlideLayer);
        ViewModel.SetTransitionPlayer(_transitionPlayer.PlayAsync);

        ProjectionRoot.SizeChanged += OnProjectionRootSizeChanged;
        Closed += (_, _) => ViewModel.SetTransitionPlayer(null);
    }

    private void OnProjectionRootSizeChanged(object sender, Microsoft.UI.Xaml.SizeChangedEventArgs e)
    {
        ViewModel.UpdateWindowSize(e.NewSize.Width, e.NewSize.Height);
    }
}
