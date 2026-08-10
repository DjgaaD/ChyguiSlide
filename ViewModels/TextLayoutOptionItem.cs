using CommunityToolkit.Mvvm.ComponentModel;
using ChyguiSlide.Services.Models;

namespace ChyguiSlide.ViewModels;

public sealed partial class TextLayoutOptionItem : ObservableObject
{
    public TextLayoutMode Mode { get; }
    public string Title { get; }
    public string Description { get; }
    public string PreviewLine1 { get; }
    public string PreviewLine2 { get; }
    public string PreviewLine3 { get; }
    public string PreviewLine4 { get; }
    public string PreviewLine5 { get; }
    public string PreviewLine6 { get; }
    public double PreviewFontSize { get; }
    public bool ShowExtraLines { get; }

    [ObservableProperty]
    private bool isSelected;

    public TextLayoutOptionItem(
        TextLayoutMode mode,
        string previewLine1,
        string previewLine2,
        string previewLine3,
        string previewLine4,
        string previewLine5,
        string previewLine6,
        double previewFontSize,
        bool showExtraLines)
    {
        Mode = mode;
        Title = mode.GetTitle();
        Description = mode.GetDescription();
        PreviewLine1 = previewLine1;
        PreviewLine2 = previewLine2;
        PreviewLine3 = previewLine3;
        PreviewLine4 = previewLine4;
        PreviewLine5 = previewLine5;
        PreviewLine6 = previewLine6;
        PreviewFontSize = previewFontSize;
        ShowExtraLines = showExtraLines;
    }
}
