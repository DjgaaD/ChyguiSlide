using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ChyguiSlide.Services.Models;
using Windows.System;

namespace ChyguiSlide.ViewModels;

public sealed partial class HotkeyBindingItem : ObservableObject
{
    private readonly Func<HotkeyBindingItem, HotkeyBinding, Task> _onChanged;
    private readonly Action<HotkeyBindingItem> _onStartListening;

    public AppHotkeyAction Action { get; }
    public string Title { get; }
    public string Description { get; }

    [ObservableProperty]
    private HotkeyBinding binding;

    [ObservableProperty]
    private bool isListening;

    [ObservableProperty]
    private string keyDisplay = "Не задано";

    public IRelayCommand StartListeningCommand { get; }
    public IRelayCommand ClearCommand { get; }

    public HotkeyBindingItem(
        AppHotkeyAction action,
        HotkeyBinding binding,
        Func<HotkeyBindingItem, HotkeyBinding, Task> onChanged,
        Action<HotkeyBindingItem> onStartListening)
    {
        Action = action;
        Title = HotkeyBinding.GetActionTitle(action);
        Description = HotkeyBinding.GetActionDescription(action);
        this.binding = binding;
        KeyDisplay = binding.ToDisplayString();
        _onChanged = onChanged;
        _onStartListening = onStartListening;

        StartListeningCommand = new RelayCommand(StartListening);
        ClearCommand = new RelayCommand(() => _ = ApplyAsync(HotkeyBinding.Create(VirtualKey.None)));
    }

    public void CancelListening()
    {
        IsListening = false;
        KeyDisplay = Binding.ToDisplayString();
    }

    public async Task ApplyAsync(HotkeyBinding newBinding)
    {
        IsListening = false;
        Binding = newBinding;
        KeyDisplay = newBinding.ToDisplayString();
        await _onChanged(this, newBinding);
    }

    private void StartListening()
    {
        IsListening = true;
        KeyDisplay = "Нажмите клавишу…";
        _onStartListening(this);
    }
}

public enum SettingsSection
{
    Projection,
    Camera,
    Interface,
    Themes,
    Hotkeys,
    Backup,
    About
}

public sealed record SettingsNavItem(string Title, string Icon, SettingsSection Section);
