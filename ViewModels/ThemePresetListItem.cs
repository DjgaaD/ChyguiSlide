using CommunityToolkit.Mvvm.ComponentModel;
using ChyguiSlide.Data.Entities;

namespace ChyguiSlide.ViewModels;

public sealed partial class ThemePresetListItem : ObservableObject
{
    public ThemePresetListItem(ThemePreset preset)
    {
        Preset = preset ?? throw new ArgumentNullException(nameof(preset));
    }

    public ThemePreset Preset { get; private set; }

    public Guid Id => Preset.Id;

    public string Name => Preset.Name;

    [ObservableProperty]
    private bool isSelected;

    [ObservableProperty]
    private bool isHovering;

    [ObservableProperty]
    private bool showEditButton;

    partial void OnIsHoveringChanged(bool value) => ShowEditButton = value;

    public void ReplacePreset(ThemePreset preset)
    {
        Preset = preset ?? throw new ArgumentNullException(nameof(preset));
        OnPropertyChanged(nameof(Preset));
        OnPropertyChanged(nameof(Name));
        OnPropertyChanged(nameof(Id));
    }
}
