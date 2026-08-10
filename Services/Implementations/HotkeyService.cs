using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using ChyguiSlide.Services.Abstractions;
using ChyguiSlide.Services.Models;
using Windows.System;

namespace ChyguiSlide.Services.Implementations;

public sealed class HotkeyService : IHotkeyService
{
    private readonly IDisplaySettingsService _displaySettingsService;
    private readonly object _sync = new();
    private Dictionary<AppHotkeyAction, HotkeyBinding> _cache;

    public event EventHandler? HotkeysChanged;

    public HotkeyService(IDisplaySettingsService displaySettingsService)
    {
        _displaySettingsService = displaySettingsService;
        _cache = CreateDefaults();
        _ = RefreshFromStorageAsync();
    }

    public async Task<IReadOnlyDictionary<AppHotkeyAction, HotkeyBinding>> GetAllAsync()
    {
        await RefreshFromStorageAsync();
        lock (_sync)
        {
            return new Dictionary<AppHotkeyAction, HotkeyBinding>(_cache);
        }
    }

    public async Task<HotkeyBinding> GetAsync(AppHotkeyAction action)
    {
        var map = await GetAllAsync();
        return map[action];
    }

    public async Task SetAsync(AppHotkeyAction action, HotkeyBinding binding)
    {
        lock (_sync)
        {
            foreach (var pair in _cache)
            {
                if (pair.Key != action && pair.Value.Equals(binding))
                {
                    _cache[pair.Key] = HotkeyBinding.Create(VirtualKey.None);
                }
            }

            _cache[action] = binding;
        }

        Dictionary<AppHotkeyAction, HotkeyBinding> snapshot;
        lock (_sync)
        {
            snapshot = new Dictionary<AppHotkeyAction, HotkeyBinding>(_cache);
        }

        await PersistAsync(snapshot);
        HotkeysChanged?.Invoke(this, EventArgs.Empty);
    }

    public async Task ResetDefaultsAsync()
    {
        var map = CreateDefaults();
        lock (_sync)
        {
            _cache = map;
        }

        await PersistAsync(map);
        HotkeysChanged?.Invoke(this, EventArgs.Empty);
    }

    public bool TryMatch(VirtualKey key, bool ctrl, bool alt, bool shift, out AppHotkeyAction action)
    {
        action = default;
        lock (_sync)
        {
            // 1) Точное совпадение с модификаторами
            foreach (var pair in _cache)
            {
                if (pair.Value.Key != VirtualKey.None && pair.Value.Matches(key, ctrl, alt, shift))
                {
                    action = pair.Key;
                    return true;
                }
            }

            // 2) Fallback: та же клавиша без модификаторов в биндинге
            // (GetKeyState/GetAsyncKeyState в LL-хуке иногда врёт для Shift на стрелках)
            foreach (var pair in _cache)
            {
                if (pair.Value.Key != VirtualKey.None
                    && pair.Value.Key == key
                    && !pair.Value.Ctrl
                    && !pair.Value.Alt
                    && !pair.Value.Shift)
                {
                    action = pair.Key;
                    return true;
                }
            }

            // 3) Fallback по числовому vkCode
            var code = (int)key;
            foreach (var pair in _cache)
            {
                if (pair.Value.Key != VirtualKey.None && (int)pair.Value.Key == code
                    && !pair.Value.Ctrl && !pair.Value.Alt && !pair.Value.Shift)
                {
                    action = pair.Key;
                    return true;
                }
            }
        }

        return false;
    }

    private async Task RefreshFromStorageAsync()
    {
        var loaded = CreateDefaults();
        foreach (AppHotkeyAction action in Enum.GetValues<AppHotkeyAction>())
        {
            var stored = await _displaySettingsService.GetHotkeyAsync(action);
            if (stored is not null)
            {
                loaded[action] = stored;
            }
        }

        lock (_sync)
        {
            _cache = loaded;
        }
    }

    private async Task PersistAsync(Dictionary<AppHotkeyAction, HotkeyBinding> map)
    {
        foreach (var pair in map)
        {
            await _displaySettingsService.SetHotkeyAsync(pair.Key, pair.Value);
        }
    }

    private static Dictionary<AppHotkeyAction, HotkeyBinding> CreateDefaults()
    {
        var map = new Dictionary<AppHotkeyAction, HotkeyBinding>();
        foreach (AppHotkeyAction action in Enum.GetValues<AppHotkeyAction>())
        {
            map[action] = HotkeyBinding.DefaultFor(action);
        }

        return map;
    }
}
