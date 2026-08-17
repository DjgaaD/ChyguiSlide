using ChyguiSlide.Controls;
using ChyguiSlide.ViewModels;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using System.Runtime.InteropServices;
using Microsoft.UI.Windowing;

namespace ChyguiSlide.Windows;

public sealed partial class ProjectionWindow : Window
{
    /// <summary>DWMWA_EXCLUDED_FROM_PEEK — окно не сворачивается в Aero Peek панели задач.</summary>
    private const int DwmwaExcludedFromPeek = 12;

    /// <summary>WM_NCHITTEST — нужно вернуть HTTRANSPARENT, чтобы курсор не «накрывался» окном.</summary>
    private const int WmNchitTest = 0x0084;
    private const IntPtr HtTransparent = -1;
    public ProjectionDisplayViewModel ViewModel { get; }

    public Grid StageHostGrid => StageHost;

    public ProjectionStageView? Stage { get; private set; }

    public MediaPlayerElement? VideoPlayerElement => Stage?.VideoPlayerElement;

    public MediaPlayerElement? BackgroundVideoPlayerElement => Stage?.BackgroundVideoPlayerElement;

    public Image? NdiVideoImageElement => Stage?.NdiVideoImageElement;

    public Grid? ContentHostGrid => Stage?.ContentHostGrid;

    public Grid? ProjectionRootGrid => Stage?.ProjectionRootGrid;

    private int _cursorVisibilityCount = 0;
    private IntPtr _blankCursor = IntPtr.Zero;
    private IntPtr _defaultCursor = IntPtr.Zero;
    private IntPtr _originalCursor = IntPtr.Zero;
    private bool _cursorHidden;

    public ProjectionWindow(ProjectionDisplayViewModel viewModel)
    {
        InitializeComponent();
        ViewModel = viewModel;
        SystemBackdrop = null;

        // Создаём пустой курсор
        _blankCursor = CreateBlankCursor();
        _defaultCursor = LoadCursor(IntPtr.Zero, 32512); // IDC_ARROW

        // Устанавливаем пустой курсор при открытии окна
        this.Activated += OnWindowActivated;
        this.Closed += OnWindowClosed;
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

    private void OnWindowActivated(object sender, WindowActivatedEventArgs e)
    {
        // Скрываем курсор при активации окна проектора через SetClassLongPtr
        var hwnd = GetWindowHandle();
        if (hwnd != IntPtr.Zero)
        {
            _originalCursor = SetClassLongPtr(hwnd, GCLP_HCURSOR, _blankCursor);
            _cursorHidden = true;
        }
    }

    private void OnWindowClosed(object sender, WindowEventArgs e)
    {
        // Возвращаем оригинальный курсор при закрытии окна
        var hwnd = GetWindowHandle();
        if (hwnd != IntPtr.Zero && _originalCursor != IntPtr.Zero)
        {
            SetClassLongPtr(hwnd, GCLP_HCURSOR, _originalCursor);
            _cursorHidden = false;
        }
    }

    public void ExcludeFromAeroPeek()
    {
        try
        {
            var hwnd = GetWindowHandle();
            System.Diagnostics.Debug.WriteLine($"[Aero Peek] HWND: 0x{hwnd.ToInt64():X}");

            if (hwnd == IntPtr.Zero)
            {
                System.Diagnostics.Debug.WriteLine("[Aero Peek] HWND is zero, skipping");
                return;
            }

            // Попробуем установить окно как owner главного окна
            var mainWindowHandle = App.MainWindowHandle;
            if (mainWindowHandle != IntPtr.Zero)
            {
                System.Diagnostics.Debug.WriteLine($"[Aero Peek] Setting owner to main window: 0x{mainWindowHandle.ToInt64():X}");
                var result = SetWindowLong(hwnd, GWL_HWNDPARENT, mainWindowHandle);
                System.Diagnostics.Debug.WriteLine($"[Aero Peek] SetWindowLong result: 0x{result.ToInt64():X}");
            }

            // Также попробуем DWMWA_EXCLUDED_FROM_PEEK
            var attributeValue = 1; // TRUE
            var hr = DwmSetWindowAttribute(hwnd, DwmwaExcludedFromPeek, ref attributeValue, sizeof(int));

            System.Diagnostics.Debug.WriteLine($"[Aero Peek] DwmSetWindowAttribute HRESULT: 0x{hr:X} ({hr})");

            if (hr != 0)
            {
                System.Diagnostics.Debug.WriteLine($"[Aero Peek] DwmSetWindowAttribute failed with HRESULT: 0x{hr:X}");
            }
            else
            {
                System.Diagnostics.Debug.WriteLine("[Aero Peek] Successfully excluded from Aero Peek");
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[Aero Peek] Exception: {ex.Message}");
            System.Diagnostics.Debug.WriteLine($"[Aero Peek] StackTrace: {ex.StackTrace}");
        }
    }

    private IntPtr GetWindowHandle()
    {
        // Получаем HWND окна через COM interop
        try
        {
            var nativeWindow = (object)this;
            var windowNative = (IWindowNative)nativeWindow;
            IntPtr hwnd;
            windowNative.WindowHandle(out hwnd);
            return hwnd;
        }
        catch
        {
            return IntPtr.Zero;
        }
    }

    [ComImport]
    [Guid("EECDBF0E-BAE9-4CB6-A68D-36682EBC0869")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IWindowNative
    {
        void WindowHandle(out IntPtr hwnd);
    }

    [DllImport("user32.dll")]
    private static extern int ShowCursor(bool bShow);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr SetClassLongPtr(IntPtr hWnd, int nIndex, IntPtr dwNewLong);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr LoadCursor(IntPtr hInstance, int lpCursorName);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr CreateIconIndirect(ref ICONINFO piconinfo);

    [DllImport("gdi32.dll", SetLastError = true)]
    private static extern IntPtr CreateBitmap(int nWidth, int nHeight, int nPlanes, int nBitsPerPixel, IntPtr lpBits);

    [DllImport("dwmapi.dll", SetLastError = true)]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int dwAttribute, ref int pvAttribute, int cbAttribute);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr GetWindowLong(IntPtr hWnd, int nIndex);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr SetWindowLong(IntPtr hWnd, int nIndex, IntPtr dwNewLong);

    private const int GCLP_HCURSOR = -12;
    private const int GWL_EXSTYLE = -20;
    private const int GWL_HWNDPARENT = -8;
    private const int WS_EX_TOOLWINDOW = 0x00000080;

    private IntPtr CreateBlankCursor()
    {
        // Создаём пустой курсор через CreateIconIndirect
        var iconInfo = new ICONINFO
        {
            fIcon = false, // Это курсор, не иконка
            xHotspot = 0,
            yHotspot = 0,
            hbmMask = CreateBitmap(1, 1, 1, 1, IntPtr.Zero),
            hbmColor = IntPtr.Zero
        };

        return CreateIconIndirect(ref iconInfo);
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct ICONINFO
    {
        public bool fIcon;
        public int xHotspot;
        public int yHotspot;
        public IntPtr hbmMask;
        public IntPtr hbmColor;
    }
}
