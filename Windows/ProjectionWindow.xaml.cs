using ChyguiSlide.Controls;
using ChyguiSlide.ViewModels;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace ChyguiSlide.Windows;

public sealed partial class ProjectionWindow : Window
{
    public ProjectionDisplayViewModel ViewModel { get; }

    public Grid StageHostGrid => StageHost;

    public ProjectionStageView? Stage { get; private set; }

    public MediaPlayerElement? VideoPlayerElement => Stage?.VideoPlayerElement;

    public MediaPlayerElement? BackgroundVideoPlayerElement => Stage?.BackgroundVideoPlayerElement;

    public Image? NdiVideoImageElement => Stage?.NdiVideoImageElement;

    public Grid? ContentHostGrid => Stage?.ContentHostGrid;

    public Grid? ProjectionRootGrid => Stage?.ProjectionRootGrid;

    public ProjectionWindow(ProjectionDisplayViewModel viewModel)
    {
        InitializeComponent();
        ViewModel = viewModel;
        SystemBackdrop = null;
    }

    public void AttachStage(ProjectionStageView stage)
    {
        DetachStage();
        Stage = stage;
        StageHost.Children.Add(stage);
    }

    public ProjectionStageView? DetachStage()
    {
        var stage = Stage;
        if (stage is null)
        {
            return null;
        }

        StageHost.Children.Remove(stage);
        Stage = null;
        return stage;
    }

    public void SetVideoPlayer(MediaPlayerElement? videoPlayer)
        => Stage?.SetVideoPlayer(videoPlayer);
}
