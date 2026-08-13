using System.Runtime.InteropServices;
using Microsoft.UI;
using Microsoft.UI.Windowing;
using WinRT.Interop;

namespace ChyguiSlide.Services;

/// <summary>
/// Иконка в заголовке окна и на панели задач (WinUI + Win32 fallback).
/// </summary>
internal static class AppWindowIconHelper
{
    private const int WM_SETICON = 0x0080;
    private const int ICON_SMALL = 0;
    private const int ICON_BIG = 1;
    private const uint IMAGE_ICON = 1;
    private const uint LR_LOADFROMFILE = 0x0010;
    private const uint LR_DEFAULTSIZE = 0x0040;

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern IntPtr LoadImage(
        IntPtr hInst,
        string name,
        uint type,
        int cx,
        int cy,
        uint fuLoad);

    [DllImport("user32.dll", CharSet = CharSet.Auto)]
    private static extern IntPtr SendMessage(IntPtr hWnd, int msg, IntPtr wParam, IntPtr lParam);

    public static bool TryApply(Window window, Action<string>? log = null)
    {
        var hwnd = WindowNative.GetWindowHandle(window);
        var windowId = Win32Interop.GetWindowIdFromWindow(hwnd);
        var appWindow = AppWindow.GetFromWindowId(windowId);
        appWindow.Title = AppVersionInfo.ProductName;

        var iconPath = ResolveIconPath();
        if (iconPath is null)
        {
            log?.Invoke("Иконка не найдена (Assets\\AppIcon.ico).");
            return TryApplyEmbeddedExeIcon(hwnd, log);
        }

        log?.Invoke($"Иконка окна: {iconPath}");

        var applied = false;
        try
        {
            appWindow.SetIcon(iconPath);
            applied = true;
        }
        catch (Exception ex)
        {
            log?.Invoke($"AppWindow.SetIcon: {ex.Message}");
        }

        applied |= ApplyWin32Icon(hwnd, iconPath, log);
        return applied;
    }

    private static string? ResolveIconPath()
    {
        foreach (var path in new[]
                 {
                     Path.Combine(AppContext.BaseDirectory, "Assets", "AppIcon.ico"),
                     Path.Combine(AppContext.BaseDirectory, "AppIcon.ico"),
                 })
        {
            if (File.Exists(path))
            {
                return Path.GetFullPath(path);
            }
        }

        return null;
    }

    private static bool ApplyWin32Icon(IntPtr hwnd, string iconPath, Action<string>? log)
    {
        var applied = false;

        var small = LoadImage(IntPtr.Zero, iconPath, IMAGE_ICON, 16, 16, LR_LOADFROMFILE);
        if (small != IntPtr.Zero)
        {
            SendMessage(hwnd, WM_SETICON, (IntPtr)ICON_SMALL, small);
            applied = true;
        }

        var big = LoadImage(IntPtr.Zero, iconPath, IMAGE_ICON, 32, 32, LR_LOADFROMFILE);
        if (big == IntPtr.Zero)
        {
            big = LoadImage(IntPtr.Zero, iconPath, IMAGE_ICON, 0, 0, LR_LOADFROMFILE | LR_DEFAULTSIZE);
        }

        if (big != IntPtr.Zero)
        {
            SendMessage(hwnd, WM_SETICON, (IntPtr)ICON_BIG, big);
            applied = true;
        }

        if (!applied)
        {
            log?.Invoke($"LoadImage не смог загрузить ICO: {iconPath}");
        }

        return applied;
    }

    private static bool TryApplyEmbeddedExeIcon(IntPtr hwnd, Action<string>? log)
    {
        var exePath = Environment.ProcessPath;
        if (string.IsNullOrWhiteSpace(exePath) || !File.Exists(exePath))
        {
            return false;
        }

        exePath = Path.GetFullPath(exePath);
        log?.Invoke($"Иконка из exe: {exePath}");
        return ApplyWin32Icon(hwnd, exePath, log);
    }
}
