using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using ChyguiSlide.Services.Abstractions;
using ChyguiSlide.Services.Models;
using ChyguiSlide.ViewModels;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Windows.System;

namespace ChyguiSlide.Services.Implementations;

/// <summary>
/// Глобальный хук клавиатуры, пока любое окно приложения на переднем плане.
/// </summary>
public sealed class HotkeyDispatcher : IDisposable
{
    private const int WhKeyboardLl = 13;
    private const int WmKeydown = 0x0100;
    private const int WmSyskeydown = 0x0104;
    private const int VkShift = 0x10;
    private const int VkControl = 0x11;
    private const int VkMenu = 0x12;

    private readonly IServiceProvider _services;
    private readonly IHotkeyService _hotkeyService;
    private readonly Microsoft.UI.Dispatching.DispatcherQueue _dispatcher;
    private readonly LowLevelKeyboardProc _proc;
    private IntPtr _hookId = IntPtr.Zero;
    private bool _disposed;

    public HotkeyDispatcher(IServiceProvider services, IHotkeyService hotkeyService)
    {
        _services = services;
        _hotkeyService = hotkeyService;
        _dispatcher = App.MainDispatcherQueue ?? Microsoft.UI.Dispatching.DispatcherQueue.GetForCurrentThread()
            ?? throw new InvalidOperationException("DispatcherQueue недоступен.");
        _proc = HookCallback;
        _hookId = SetHook(_proc);
    }

    public void AttachToMain(UIElement? _) => EnsureHook();
    public void DetachMain() { }
    public void AttachToProjection(UIElement? _) => EnsureHook();
    public void DetachProjection() { }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        if (_hookId != IntPtr.Zero)
        {
            UnhookWindowsHookEx(_hookId);
            _hookId = IntPtr.Zero;
        }
    }

    private void EnsureHook()
    {
        if (_hookId == IntPtr.Zero && !_disposed)
        {
            _hookId = SetHook(_proc);
        }
    }

    private IntPtr HookCallback(int nCode, IntPtr wParam, IntPtr lParam)
    {
        if (nCode >= 0 && (wParam == (IntPtr)WmKeydown || wParam == (IntPtr)WmSyskeydown))
        {
            var vkCode = Marshal.ReadInt32(lParam);
            if (IsOurAppForeground() && TryHandleKey(vkCode))
            {
                return (IntPtr)1;
            }
        }

        return CallNextHookEx(_hookId, nCode, wParam, lParam);
    }

    private bool TryHandleKey(int vkCode)
    {
        var key = (VirtualKey)vkCode;
        if (HotkeyBinding.IsModifierKey(key))
        {
            return false;
        }

        var settings = _services.GetService<ThemePresetEditorViewModel>();
        if (settings?.IsCapturingHotkey == true)
        {
            return false;
        }

        // GetAsyncKeyState надёжнее GetKeyState внутри LL-хука
        var ctrl = (GetAsyncKeyState(VkControl) & 0x8000) != 0;
        var alt = (GetAsyncKeyState(VkMenu) & 0x8000) != 0;
        var shift = (GetAsyncKeyState(VkShift) & 0x8000) != 0;

        if (!_hotkeyService.TryMatch(key, ctrl, alt, shift, out var action))
        {
            return false;
        }

        // Не перехватывать стрелки/прочее, пока пользователь печатает в поле ввода.
        // F1–F5 / Esc / поиск — всегда: иначе поле ввода «съедает» навигацию и старт показа.
        if (IsTextInputFocused()
            && action is not AppHotkeyAction.StartShow
            && action is not AppHotkeyAction.EndShow
            && action is not AppHotkeyAction.FocusBibleSearch
            && action is not AppHotkeyAction.GoToCatalog
            && action is not AppHotkeyAction.GoToBible
            && action is not AppHotkeyAction.GoToAnnouncements)
        {
            return false;
        }

        // ← → только во время показа на проекторе — иначе мешают редактору и спискам
        if (action is AppHotkeyAction.NextSlide or AppHotkeyAction.PreviousSlide)
        {
            var projection = _services.GetService<IProjectionDisplayService>();
            if (projection is null || !projection.IsOpen)
            {
                return false;
            }
        }

        Debug.WriteLine($"[HotkeyDispatcher] key={vkCode} ({key}) → {action}");
        _dispatcher.TryEnqueue(() => _ = DispatchAsync(action));
        return true;
    }

    private async Task DispatchAsync(AppHotkeyAction action)
    {
        try
        {
            if (action is AppHotkeyAction.GoToCatalog
                or AppHotkeyAction.GoToBible
                or AppHotkeyAction.GoToAnnouncements)
            {
                var main = _services.GetRequiredService<MainViewModel>();
                switch (action)
                {
                    case AppHotkeyAction.GoToCatalog:
                        main.NavigateToCatalog();
                        break;
                    case AppHotkeyAction.GoToBible:
                        main.NavigateToBible();
                        break;
                    case AppHotkeyAction.GoToAnnouncements:
                        main.NavigateToAnnouncements();
                        break;
                }

                return;
            }

            if (action == AppHotkeyAction.FocusBibleSearch)
            {
                var main = _services.GetRequiredService<MainViewModel>();
                if (main.IsOnCatalogPage)
                {
                    var catalog = _services.GetRequiredService<CatalogViewModel>();
                    await catalog.InitializeAsync();
                    catalog.RequestSearchFocus();
                }
                else if (main.IsOnBiblePage)
                {
                    var bible = _services.GetRequiredService<BibleViewModel>();
                    await bible.InitializeAsync();
                    bible.RequestSearchFocus();
                }

                return;
            }

            var live = _services.GetRequiredService<LiveControlViewModel>();
            if (!live.IsInitialized)
            {
                await live.InitializeAsync();
            }

            live.ExecuteHotkey(action);
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[HotkeyDispatcher] {ex}");
        }
    }

    private static bool IsOurAppForeground()
    {
        var foreground = GetForegroundWindow();
        if (foreground == IntPtr.Zero)
        {
            return false;
        }

        _ = GetWindowThreadProcessId(foreground, out var processId);
        return processId == (uint)Environment.ProcessId;
    }

    /// <summary>Есть caret (TextBox / AutoSuggest и т.п.) — клавиши должны идти в поле ввода.</summary>
    private static bool IsTextInputFocused()
    {
        var foreground = GetForegroundWindow();
        if (foreground == IntPtr.Zero)
        {
            return false;
        }

        var threadId = GetWindowThreadProcessId(foreground, out _);
        var info = new GuiThreadInfo { CbSize = Marshal.SizeOf<GuiThreadInfo>() };
        if (!GetGUIThreadInfo(threadId, ref info))
        {
            return false;
        }

        return info.HwndCaret != IntPtr.Zero;
    }

    private IntPtr SetHook(LowLevelKeyboardProc proc)
    {
        using var curProcess = Process.GetCurrentProcess();
        IntPtr moduleHandle = IntPtr.Zero;
        try
        {
            ProcessModule? curModule = null;
            try
            {
                curModule = curProcess.MainModule;
            }
            catch
            {
                curModule = null;
            }

            if (curModule != null && !string.IsNullOrEmpty(curModule.ModuleName))
            {
                moduleHandle = GetModuleHandle(curModule.ModuleName);
            }
        }
        catch
        {
            moduleHandle = IntPtr.Zero;
        }

        return SetWindowsHookEx(WhKeyboardLl, proc, moduleHandle, 0);
    }

    private delegate IntPtr LowLevelKeyboardProc(int nCode, IntPtr wParam, IntPtr lParam);

    [StructLayout(LayoutKind.Sequential)]
    private struct GuiThreadInfo
    {
        public int CbSize;
        public int Flags;
        public IntPtr HwndActive;
        public IntPtr HwndFocus;
        public IntPtr HwndCapture;
        public IntPtr HwndMenu;
        public IntPtr HwndCaret;
        public int CaretLeft;
        public int CaretTop;
        public int CaretRight;
        public int CaretBottom;
    }

    [DllImport("user32.dll", CharSet = CharSet.Auto, SetLastError = true)]
    private static extern IntPtr SetWindowsHookEx(int idHook, LowLevelKeyboardProc lpfn, IntPtr hMod, uint dwThreadId);

    [DllImport("user32.dll", CharSet = CharSet.Auto, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool UnhookWindowsHookEx(IntPtr hhk);

    [DllImport("user32.dll", CharSet = CharSet.Auto, SetLastError = true)]
    private static extern IntPtr CallNextHookEx(IntPtr hhk, int nCode, IntPtr wParam, IntPtr lParam);

    [DllImport("kernel32.dll", CharSet = CharSet.Auto, SetLastError = true)]
    private static extern IntPtr GetModuleHandle(string lpModuleName);

    [DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint lpdwProcessId);

    [DllImport("user32.dll")]
    private static extern short GetAsyncKeyState(int vKey);

    [DllImport("user32.dll")]
    private static extern bool GetGUIThreadInfo(uint idThread, ref GuiThreadInfo info);
}
