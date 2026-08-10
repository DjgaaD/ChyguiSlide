using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using ChyguiSlide.Services.Models;
using Windows.System;

namespace ChyguiSlide.Services.Abstractions;

public interface IHotkeyService
{
    event EventHandler? HotkeysChanged;

    Task<IReadOnlyDictionary<AppHotkeyAction, HotkeyBinding>> GetAllAsync();
    Task<HotkeyBinding> GetAsync(AppHotkeyAction action);
    Task SetAsync(AppHotkeyAction action, HotkeyBinding binding);
    Task ResetDefaultsAsync();
    bool TryMatch(VirtualKey key, bool ctrl, bool alt, bool shift, out AppHotkeyAction action);
}
