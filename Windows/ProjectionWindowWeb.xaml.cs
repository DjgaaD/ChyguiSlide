using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using ChyguiSlide.ViewModels;
using Microsoft.UI;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.Web.WebView2.Core;
using Windows.Graphics;
using WinRT.Interop;

namespace ChyguiSlide.Windows;

public sealed partial class ProjectionWindowWeb : Window
{
    private const int DwmwaExcludedFromPeek = 12;
    private const int GwlHwndParent = -8;
    private const int SmCxCursor = 13;
    private const int SmCyCursor = 14;
    private const uint SpiSetCursors = 0x0057;

    // OCR_* — SetSystemCursor заменяет системные курсоры (CSS cursor:none в WinUI WebView2 не работает).
    private static readonly uint[] SystemCursorIds =
    [
        32512, // OCR_NORMAL
        32513, // OCR_IBEAM
        32514, // OCR_WAIT
        32515, // OCR_CROSS
        32516, // OCR_UP
        32642, // OCR_SIZENWSE
        32643, // OCR_SIZENESW
        32644, // OCR_SIZEWE
        32645, // OCR_SIZENS
        32646, // OCR_SIZEALL
        32648, // OCR_NO
        32649, // OCR_HAND
        32650, // OCR_APPSTARTING
    ];

    public ProjectionDisplayViewModel ViewModel { get; }
    private WebProjectionAdapter? _adapter;
    private bool _navigationHooked;

    private bool _cursorWatchActive;
    private bool _systemCursorsBlanked;
    private bool _showCursorHidden;
    private DispatcherQueueTimer? _cursorTimer;
    private int _projectionX;
    private int _projectionY;
    private int _projectionWidth;
    private int _projectionHeight;
    private bool _hasProjectionBounds;

    public ProjectionWindowWeb(ProjectionDisplayViewModel viewModel)
    {
        InitializeComponent();
        ViewModel = viewModel;
        SystemBackdrop = null;
        Closed += OnClosed;
        Activated += OnActivated;

        InitializeWebView();
    }

    private async void InitializeWebView()
    {
        try
        {
            ChyguiSlide.Data.InteractionLogger.Log("[ProjectionWindowWeb] Starting WebView2 initialization...");
            if (!_navigationHooked)
            {
                _navigationHooked = true;
                WebView.NavigationCompleted += OnNavigationCompleted;
            }

            await WebProjectionRuntime.PrepareWebViewAsync(WebView, profileName: "projection");
            ChyguiSlide.Data.InteractionLogger.Log("[ProjectionWindowWeb] WebView2 initialized and HTML loaded");
        }
        catch (Exception ex)
        {
            ChyguiSlide.Data.InteractionLogger.Log($"[ProjectionWindowWeb] WebView2 initialization failed: {ex.Message}");
            ChyguiSlide.Data.InteractionLogger.Log($"[ProjectionWindowWeb] Stack trace: {ex.StackTrace}");
        }
    }

    private void OnNavigationCompleted(object? sender, CoreWebView2NavigationCompletedEventArgs e)
    {
        if (!e.IsSuccess)
        {
            ChyguiSlide.Data.InteractionLogger.Log(
                $"[ProjectionWindowWeb] Navigation failed: {e.WebErrorStatus}");
            return;
        }

        StartCursorSuppression();

        if (_adapter is not null || WebView.CoreWebView2 is null)
        {
            _adapter?.MarkPageReady();
            return;
        }

        _adapter = new WebProjectionAdapter(WebView.CoreWebView2, ViewModel);
        ChyguiSlide.Data.InteractionLogger.Log("[ProjectionWindowWeb] WebProjectionAdapter created after NavigationCompleted");
        _adapter.MarkPageReady();
    }

    private void OnActivated(object sender, WindowActivatedEventArgs e)
    {
        if (e.WindowActivationState != WindowActivationState.Deactivated)
        {
            StartCursorSuppression();
        }
    }

    private void OnClosed(object sender, WindowEventArgs args)
    {
        Activated -= OnActivated;
        StopCursorSuppression();
        DisposeAdapter();
        if (_navigationHooked)
        {
            try
            {
                WebView.NavigationCompleted -= OnNavigationCompleted;
            }
            catch
            {
                // ignore
            }

            _navigationHooked = false;
        }
    }

    public void DisposeAdapter()
    {
        _adapter?.Dispose();
        _adapter = null;
    }

    public void ExcludeFromAeroPeek()
    {
        try
        {
            var hwnd = GetWindowHandle();
            if (hwnd == IntPtr.Zero)
            {
                return;
            }

            var mainWindowHandle = App.MainWindowHandle;
            if (mainWindowHandle != IntPtr.Zero)
            {
                _ = SetWindowLongPtr(hwnd, GwlHwndParent, mainWindowHandle);
            }

            var attributeValue = 1;
            _ = DwmSetWindowAttribute(hwnd, DwmwaExcludedFromPeek, ref attributeValue, sizeof(int));
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[ProjectionWindowWeb Aero Peek] Exception: {ex.Message}");
        }
    }

    private IntPtr GetWindowHandle()
    {
        try
        {
            return WindowNative.GetWindowHandle(this);
        }
        catch
        {
            return IntPtr.Zero;
        }
    }

    public void SetFullScreenOnDisplay(DisplayArea? displayArea)
    {
        if (displayArea is null)
        {
            return;
        }

        var appWindow = GetAppWindow();
        if (appWindow is null)
        {
            return;
        }

        // OuterBounds = весь монитор (не WorkArea).
        var bounds = displayArea.OuterBounds;
        _projectionX = bounds.X;
        _projectionY = bounds.Y;
        _projectionWidth = bounds.Width;
        _projectionHeight = bounds.Height;
        _hasProjectionBounds = _projectionWidth > 0 && _projectionHeight > 0;

        appWindow.Move(new PointInt32(bounds.X, bounds.Y));
        appWindow.SetPresenter(AppWindowPresenterKind.FullScreen);
        StartCursorSuppression();
    }

    private AppWindow? GetAppWindow()
    {
        try
        {
            var hwnd = WindowNative.GetWindowHandle(this);
            var windowId = Win32Interop.GetWindowIdFromWindow(hwnd);
            return AppWindow.GetFromWindowId(windowId);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[ProjectionWindowWeb] GetAppWindow failed: {ex.Message}");
            return null;
        }
    }

    /// <summary>
    /// CSS cursor:none в WinUI WebView2 не поддерживается (курсор рисует хост, не Chromium).
    /// Прячем системные курсоры через SetSystemCursor, пока мышь в границах окна проекции.
    /// </summary>
    private void StartCursorSuppression()
    {
        try
        {
            _cursorWatchActive = true;
            _cursorTimer ??= DispatcherQueue.CreateTimer();
            _cursorTimer.Interval = TimeSpan.FromMilliseconds(16);
            _cursorTimer.IsRepeating = true;
            _cursorTimer.Tick -= OnCursorTimerTick;
            _cursorTimer.Tick += OnCursorTimerTick;
            if (!_cursorTimer.IsRunning)
            {
                _cursorTimer.Start();
            }

            // Сразу применить, не дожидаясь первого Tick.
            OnCursorTimerTick(_cursorTimer, EventArgs.Empty);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[ProjectionWindowWeb] StartCursorSuppression: {ex.Message}");
        }
    }

    private void StopCursorSuppression()
    {
        try
        {
            _cursorWatchActive = false;
            if (_cursorTimer is not null)
            {
                _cursorTimer.Tick -= OnCursorTimerTick;
                _cursorTimer.Stop();
                _cursorTimer = null;
            }

            RestoreSystemCursors();
        }
        catch
        {
            // ignore on teardown
        }
    }

    private void OnCursorTimerTick(DispatcherQueueTimer sender, object args)
    {
        if (!_cursorWatchActive)
        {
            return;
        }

        try
        {
            if (!GetCursorPos(out var pt))
            {
                return;
            }

            if (IsPointOverProjectionWindow(pt.X, pt.Y))
            {
                BlankSystemCursors();
            }
            else
            {
                RestoreSystemCursors();
            }
        }
        catch
        {
            // ignore
        }
    }

    private bool IsPointOverProjectionWindow(int x, int y)
    {
        if (_hasProjectionBounds)
        {
            return x >= _projectionX
                   && y >= _projectionY
                   && x < _projectionX + _projectionWidth
                   && y < _projectionY + _projectionHeight;
        }

        var appWindow = GetAppWindow();
        if (appWindow is null)
        {
            return false;
        }

        var pos = appWindow.Position;
        var size = appWindow.Size;
        if (size.Width <= 0 || size.Height <= 0)
        {
            return false;
        }

        return x >= pos.X
               && y >= pos.Y
               && x < pos.X + size.Width
               && y < pos.Y + size.Height;
    }

    private void BlankSystemCursors()
    {
        HideMouseCursor();

        if (_systemCursorsBlanked)
        {
            return;
        }

        var replaced = 0;
        foreach (var cursorId in SystemCursorIds)
        {
            // SetSystemCursor уничтожает переданный HCURSOR — каждый раз новый blank.
            var blank = CreateBlankCursor();
            if (blank == IntPtr.Zero)
            {
                ChyguiSlide.Data.InteractionLogger.Log(
                    $"[ProjectionWindowWeb] CreateBlankCursor failed, err={Marshal.GetLastWin32Error()}");
                continue;
            }

            if (!SetSystemCursor(blank, cursorId))
            {
                ChyguiSlide.Data.InteractionLogger.Log(
                    $"[ProjectionWindowWeb] SetSystemCursor({cursorId}) failed, err={Marshal.GetLastWin32Error()}");
                DestroyCursor(blank);
                continue;
            }

            replaced++;
        }

        _systemCursorsBlanked = true;
        ChyguiSlide.Data.InteractionLogger.Log(
            $"[ProjectionWindowWeb] System cursors blanked (replaced={replaced}/{SystemCursorIds.Length})");
    }

    private void RestoreSystemCursors()
    {
        ShowMouseCursor();

        if (!_systemCursorsBlanked)
        {
            return;
        }

        SystemParametersInfo(SpiSetCursors, 0, IntPtr.Zero, 0);
        _systemCursorsBlanked = false;
        ChyguiSlide.Data.InteractionLogger.Log("[ProjectionWindowWeb] System cursors restored");
    }

    private void HideMouseCursor()
    {
        if (_showCursorHidden)
        {
            return;
        }

        // Доводим счётчик ниже нуля — иначе WinUI/WebView2 снова показывает стрелку.
        var count = ShowCursor(false);
        while (count >= 0)
        {
            count = ShowCursor(false);
        }

        _showCursorHidden = true;
    }

    private void ShowMouseCursor()
    {
        if (!_showCursorHidden)
        {
            return;
        }

        var count = ShowCursor(true);
        while (count < 0)
        {
            count = ShowCursor(true);
        }

        _showCursorHidden = false;
    }

    private static IntPtr CreateBlankCursor()
    {
        // Размер обязан совпадать с SM_CXCURSOR/SM_CYCURSOR — иначе CreateCursor вернёт 0.
        var width = GetSystemMetrics(SmCxCursor);
        var height = GetSystemMetrics(SmCyCursor);
        if (width <= 0 || height <= 0)
        {
            width = 32;
            height = 32;
        }

        var stride = ((width + 15) / 16) * 2;
        var andPlane = new byte[stride * height];
        var xorPlane = new byte[stride * height];
        Array.Fill(andPlane, (byte)0xFF);

        return CreateCursor(IntPtr.Zero, 0, 0, width, height, andPlane, xorPlane);
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct Point
    {
        public int X;
        public int Y;
    }

    [DllImport("dwmapi.dll", SetLastError = true)]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int dwAttribute, ref int pvAttribute, int cbAttribute);

    [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW", SetLastError = true)]
    private static extern IntPtr SetWindowLongPtr(IntPtr hWnd, int nIndex, IntPtr dwNewLong);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr CreateCursor(
        IntPtr hInst, int xHotSpot, int yHotSpot, int nWidth, int nHeight, byte[] pvANDPlane, byte[] pvXORPlane);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool DestroyCursor(IntPtr hCursor);

    [DllImport("user32.dll")]
    private static extern int GetSystemMetrics(int nIndex);

    [DllImport("user32.dll")]
    private static extern bool GetCursorPos(out Point lpPoint);

    [DllImport("user32.dll")]
    private static extern int ShowCursor(bool bShow);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool SetSystemCursor(IntPtr hcur, uint id);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool SystemParametersInfo(uint uiAction, uint uiParam, IntPtr pvParam, uint fWinIni);
}
