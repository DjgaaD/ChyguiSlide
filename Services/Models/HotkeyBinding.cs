using System;
using System.Text;
using Windows.System;

namespace ChyguiSlide.Services.Models;

public enum AppHotkeyAction
{
    StartShow,
    EndShow,
    NextSlide,
    PreviousSlide,
    FocusBibleSearch,
    GoToCatalog,
    GoToBible,
    GoToAnnouncements
}

public sealed class HotkeyBinding : IEquatable<HotkeyBinding>
{
    public VirtualKey Key { get; init; }
    public bool Ctrl { get; init; }
    public bool Alt { get; init; }
    public bool Shift { get; init; }

    public static HotkeyBinding Create(VirtualKey key, bool ctrl = false, bool alt = false, bool shift = false)
        => new() { Key = key, Ctrl = ctrl, Alt = alt, Shift = shift };

    public static HotkeyBinding DefaultFor(AppHotkeyAction action) => action switch
    {
        AppHotkeyAction.StartShow => Create(VirtualKey.F5),
        AppHotkeyAction.EndShow => Create(VirtualKey.Escape),
        AppHotkeyAction.NextSlide => Create(VirtualKey.Right),
        AppHotkeyAction.PreviousSlide => Create(VirtualKey.Left),
        AppHotkeyAction.FocusBibleSearch => Create(VirtualKey.F4),
        AppHotkeyAction.GoToCatalog => Create(VirtualKey.F1),
        AppHotkeyAction.GoToBible => Create(VirtualKey.F2),
        AppHotkeyAction.GoToAnnouncements => Create(VirtualKey.F3),
        _ => Create(VirtualKey.None)
    };

    public static string GetActionTitle(AppHotkeyAction action) => action switch
    {
        AppHotkeyAction.StartShow => "Начать показ",
        AppHotkeyAction.EndShow => "Завершить показ",
        AppHotkeyAction.NextSlide => "Следующий слайд",
        AppHotkeyAction.PreviousSlide => "Предыдущий слайд",
        AppHotkeyAction.FocusBibleSearch => "Поиск в текущем разделе",
        AppHotkeyAction.GoToCatalog => "Раздел «Песни»",
        AppHotkeyAction.GoToBible => "Раздел «Библия»",
        AppHotkeyAction.GoToAnnouncements => "Раздел «Объявления»",
        _ => action.ToString()
    };

    public static string GetActionDescription(AppHotkeyAction action) => action switch
    {
        AppHotkeyAction.StartShow => "Открывает окно трансляции на выбранном экране.",
        AppHotkeyAction.EndShow => "Закрывает окно трансляции.",
        AppHotkeyAction.NextSlide => "Следующий слайд; у песни на последнем — закрывает показ; у Библии — следующая глава.",
        AppHotkeyAction.PreviousSlide => "Переключает на предыдущий слайд или песню.",
        AppHotkeyAction.FocusBibleSearch => "Ставит курсор в поиск на текущей странице: Песни или Библия.",
        AppHotkeyAction.GoToCatalog => "Переключает навигацию на раздел «Песни».",
        AppHotkeyAction.GoToBible => "Переключает навигацию на раздел «Библия».",
        AppHotkeyAction.GoToAnnouncements => "Переключает навигацию на раздел «Объявления».",
        _ => string.Empty
    };

    public bool Matches(VirtualKey key, bool ctrl, bool alt, bool shift)
        => Key == key && Ctrl == ctrl && Alt == alt && Shift == shift;

    public string ToDisplayString()
    {
        if (Key == VirtualKey.None)
        {
            return "Не задано";
        }

        var builder = new StringBuilder();
        if (Ctrl) builder.Append("Ctrl+");
        if (Alt) builder.Append("Alt+");
        if (Shift) builder.Append("Shift+");
        builder.Append(FormatKey(Key));
        return builder.ToString();
    }

    public string Serialize()
    {
        var builder = new StringBuilder();
        if (Ctrl) builder.Append("Ctrl+");
        if (Alt) builder.Append("Alt+");
        if (Shift) builder.Append("Shift+");
        builder.Append((int)Key);
        return builder.ToString();
    }

    public static HotkeyBinding? TryParse(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var ctrl = false;
        var alt = false;
        var shift = false;
        var keyPart = value.Trim();

        while (true)
        {
            if (keyPart.StartsWith("Ctrl+", StringComparison.OrdinalIgnoreCase))
            {
                ctrl = true;
                keyPart = keyPart[5..];
                continue;
            }

            if (keyPart.StartsWith("Alt+", StringComparison.OrdinalIgnoreCase))
            {
                alt = true;
                keyPart = keyPart[4..];
                continue;
            }

            if (keyPart.StartsWith("Shift+", StringComparison.OrdinalIgnoreCase))
            {
                shift = true;
                keyPart = keyPart[6..];
                continue;
            }

            break;
        }

        if (!int.TryParse(keyPart, out var keyCode))
        {
            if (Enum.TryParse<VirtualKey>(keyPart, true, out var namedKey))
            {
                return Create(namedKey, ctrl, alt, shift);
            }

            return null;
        }

        return Create((VirtualKey)keyCode, ctrl, alt, shift);
    }

    public static bool IsModifierKey(VirtualKey key)
        => key is VirtualKey.Control or VirtualKey.LeftControl or VirtualKey.RightControl
            or VirtualKey.Menu or VirtualKey.LeftMenu or VirtualKey.RightMenu
            or VirtualKey.Shift or VirtualKey.LeftShift or VirtualKey.RightShift
            or VirtualKey.LeftWindows or VirtualKey.RightWindows
            or VirtualKey.CapitalLock or VirtualKey.NumberKeyLock;

    private static string FormatKey(VirtualKey key) => key switch
    {
        VirtualKey.Escape => "Esc",
        VirtualKey.Left => "←",
        VirtualKey.Right => "→",
        VirtualKey.Up => "↑",
        VirtualKey.Down => "↓",
        VirtualKey.Space => "Space",
        VirtualKey.Enter => "Enter",
        VirtualKey.Back => "Backspace",
        VirtualKey.Tab => "Tab",
        VirtualKey.PageUp => "Page Up",
        VirtualKey.PageDown => "Page Down",
        VirtualKey.Home => "Home",
        VirtualKey.End => "End",
        VirtualKey.Delete => "Delete",
        VirtualKey.Insert => "Insert",
        >= VirtualKey.Number0 and <= VirtualKey.Number9 => ((char)('0' + (key - VirtualKey.Number0))).ToString(),
        >= VirtualKey.A and <= VirtualKey.Z => key.ToString(),
        >= VirtualKey.F1 and <= VirtualKey.F24 => key.ToString(),
        _ => key.ToString()
    };

    public bool Equals(HotkeyBinding? other)
    {
        if (other is null) return false;
        return Key == other.Key && Ctrl == other.Ctrl && Alt == other.Alt && Shift == other.Shift;
    }

    public override bool Equals(object? obj) => Equals(obj as HotkeyBinding);

    public override int GetHashCode() => HashCode.Combine(Key, Ctrl, Alt, Shift);

    public override string ToString() => ToDisplayString();
}
