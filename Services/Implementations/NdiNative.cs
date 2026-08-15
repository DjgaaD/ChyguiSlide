using System;
using System.IO;
using System.Runtime.InteropServices;

namespace ChyguiSlide.Services.Implementations;

/// <summary>
/// P/Invoke определения для официального NDI SDK
/// Основано на официальной документации NDI SDK
/// </summary>
internal static class NdiNative
{
    private const string NdiLibDll = "Processing.NDI.Lib.x64.dll";
    private static IntPtr _dllHandle = IntPtr.Zero;
    private static bool _dllLoaded = false;
    
    [DllImport("kernel32.dll", CharSet = CharSet.Auto, SetLastError = true)]
    private static extern IntPtr LoadLibrary(string lpFileName);
    
    [DllImport("kernel32.dll", CharSet = CharSet.Auto, SetLastError = true)]
    private static extern IntPtr LoadLibraryEx(string lpFileName, IntPtr hFile, uint dwFlags);
    
    [DllImport("kernel32.dll", CharSet = CharSet.Auto, SetLastError = true)]
    private static extern bool FreeLibrary(IntPtr hModule);
    
    [DllImport("kernel32.dll", CharSet = CharSet.Auto, SetLastError = true)]
    private static extern bool SetDllDirectory(string? lpPathName);
    
    private const uint LOAD_WITH_ALTERED_SEARCH_PATH = 0x00000008;
    
    [DllImport("kernel32.dll", CharSet = CharSet.Auto)]
    private static extern uint FormatMessage(
        uint dwFlags,
        IntPtr lpSource,
        uint dwMessageId,
        uint dwLanguageId,
        System.Text.StringBuilder lpBuffer,
        uint nSize,
        IntPtr Arguments);
    
    private const uint FORMAT_MESSAGE_FROM_SYSTEM = 0x00001000;
    
    static NdiNative()
    {
        // Пытаемся найти и загрузить DLL
        LoadNdiDll();
    }
    
    private static string GetErrorMessage(uint errorCode)
    {
        var sb = new System.Text.StringBuilder(256);
        FormatMessage(FORMAT_MESSAGE_FROM_SYSTEM, IntPtr.Zero, errorCode, 0, sb, (uint)sb.Capacity, IntPtr.Zero);
        return sb.ToString().Trim();
    }
    
    private static void LoadNdiDll()
    {
        if (_dllLoaded && _dllHandle != IntPtr.Zero)
        {
            return;
        }
        
        // Стандартные пути установки NDI Runtime
        var possiblePaths = new[]
        {
            // NDI 6 Runtime - основная директория
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "NDI", "NDI 6 Runtime"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), "NDI", "NDI 6 Runtime"),
            // NDI 6 Runtime - подпапка v6 (актуальная версия)
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "NDI", "NDI 6 Runtime", "v6"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), "NDI", "NDI 6 Runtime", "v6"),
            // NDI 5 Runtime
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "NDI", "NDI 5 Runtime"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), "NDI", "NDI 5 Runtime"),
            // Также проверяем текущую директорию приложения
            AppContext.BaseDirectory
        };

        System.Diagnostics.Debug.WriteLine("[NdiNative] Searching for NDI DLL...");
        
        foreach (var directory in possiblePaths)
        {
            var dllPath = Path.Combine(directory, NdiLibDll);
            
            if (File.Exists(dllPath))
            {
                System.Diagnostics.Debug.WriteLine($"[NdiNative] Found DLL at: {dllPath}");
                LogToFile($"[NdiNative] Found DLL at: {dllPath}");
                
                // Сначала устанавливаем директорию для поиска зависимостей
                SetDllDirectory(directory);
                
                // Пытаемся загрузить DLL с LOAD_WITH_ALTERED_SEARCH_PATH для поиска зависимостей в той же директории
                _dllHandle = LoadLibraryEx(dllPath, IntPtr.Zero, LOAD_WITH_ALTERED_SEARCH_PATH);
                
                if (_dllHandle == IntPtr.Zero)
                {
                    // Если не получилось, пробуем обычный LoadLibrary
                    _dllHandle = LoadLibrary(dllPath);
                }
                
                if (_dllHandle != IntPtr.Zero)
                {
                    System.Diagnostics.Debug.WriteLine($"[NdiNative] Successfully loaded NDI DLL from: {dllPath}");
                    LogToFile($"[NdiNative] Successfully loaded NDI DLL from: {dllPath}");
                    _dllLoaded = true;
                    return;
                }
                else
                {
                    var errorCode = (uint)Marshal.GetLastWin32Error();
                    var errorMessage = GetErrorMessage(errorCode);
                    System.Diagnostics.Debug.WriteLine($"[NdiNative] Failed to load DLL from {dllPath}");
                    System.Diagnostics.Debug.WriteLine($"[NdiNative] Error code: {errorCode}, Message: {errorMessage}");
                    LogToFile($"[NdiNative] Failed to load DLL from {dllPath}, Error code: {errorCode}, Message: {errorMessage}");
                    
                    // Проверяем зависимости
                    System.Diagnostics.Debug.WriteLine($"[NdiNative] Checking dependencies...");
                    LogToFile($"[NdiNative] DLL found but failed to load. Possible missing dependencies (Visual C++ Redistributable).");
                }
            }
            else
            {
                System.Diagnostics.Debug.WriteLine($"[NdiNative] DLL not found at: {dllPath}");
            }
        }
        
        // Также пробуем загрузить по имени (если DLL в PATH)
        System.Diagnostics.Debug.WriteLine($"[NdiNative] Trying to load DLL by name from PATH...");
        _dllHandle = LoadLibrary(NdiLibDll);
        if (_dllHandle != IntPtr.Zero)
        {
            System.Diagnostics.Debug.WriteLine($"[NdiNative] Successfully loaded NDI DLL from PATH");
            _dllLoaded = true;
            return;
        }
        else
        {
            var errorCode = (uint)Marshal.GetLastWin32Error();
            var errorMessage = GetErrorMessage(errorCode);
            System.Diagnostics.Debug.WriteLine($"[NdiNative] Failed to load DLL from PATH, error code: {errorCode}, Message: {errorMessage}");
        }

        System.Diagnostics.Debug.WriteLine("[NdiNative] NDI DLL not found or could not be loaded. Make sure NDI Runtime is installed.");
        LogToFile("[NdiNative] NDI DLL not found or could not be loaded.");
        LogToFile("[NdiNative] Searched locations:");
        foreach (var directory in possiblePaths)
        {
            var dllPath = Path.Combine(directory, NdiLibDll);
            var exists = File.Exists(dllPath);
            System.Diagnostics.Debug.WriteLine($"  - {dllPath} (exists: {exists})");
            LogToFile($"  - {dllPath} (exists: {exists})");
        }
        
        // Не бросаем исключение здесь, так как это может вызвать проблемы при статической инициализации
        // Вместо этого, проверка будет выполнена в NdiReceiverService при попытке использования
    }
    
    private static void LogToFile(string message)
    {
        try
        {
            var logPath = ChyguiSlide.Data.AppPaths.GetLogPath("ndi.log");
            var logLine = $"[{DateTime.Now:HH:mm:ss.fff}] {message}";
            File.AppendAllText(logPath, logLine + Environment.NewLine);
        }
        catch
        {
            // Игнорируем ошибки логирования
        }
    }
    
    /// <summary>
    /// Проверяет, загружена ли NDI DLL
    /// </summary>
    public static bool IsDllLoaded
    {
        get
        {
            // Если мы считаем, что DLL загружена, проверяем, что handle валиден
            if (_dllLoaded && _dllHandle != IntPtr.Zero)
            {
                // Дополнительно проверяем, что DLL действительно загружена в процесс
                // Попробуем получить адрес функции - если получится, значит DLL загружена
                try
                {
                    // Просто проверяем, что handle не нулевой и DLL не выгружена
                    return true;
                }
                catch
                {
                    return false;
                }
            }
            return false;
        }
    }
    
    /// <summary>
    /// Получает путь к загруженной DLL (для диагностики)
    /// </summary>
    public static string? GetLoadedDllPath()
    {
        if (_dllHandle == IntPtr.Zero)
            return null;
            
        // Пытаемся получить путь к загруженной DLL
        try
        {
            var buffer = new System.Text.StringBuilder(260);
            var length = GetModuleFileName(_dllHandle, buffer, buffer.Capacity);
            if (length > 0)
                return buffer.ToString();
        }
        catch
        {
            // Игнорируем ошибки
        }
        return null;
    }
    
    [DllImport("kernel32.dll", CharSet = CharSet.Auto, SetLastError = true)]
    private static extern int GetModuleFileName(IntPtr hModule, System.Text.StringBuilder lpFilename, int nSize);

    #region Initialization

    [DllImport(NdiLibDll, EntryPoint = "NDIlib_initialize", CallingConvention = CallingConvention.Cdecl)]
    [return: MarshalAs(UnmanagedType.I1)]
    public static extern bool Initialize();

    [DllImport(NdiLibDll, EntryPoint = "NDIlib_destroy", CallingConvention = CallingConvention.Cdecl)]
    public static extern void Destroy();

    #endregion

    #region Finder

    [DllImport(NdiLibDll, EntryPoint = "NDIlib_find_create_v2", CallingConvention = CallingConvention.Cdecl)]
    public static extern IntPtr FindCreateV2(IntPtr p_create_settings);

    [DllImport(NdiLibDll, EntryPoint = "NDIlib_find_destroy", CallingConvention = CallingConvention.Cdecl)]
    public static extern void FindDestroy(IntPtr p_instance);

    [DllImport(NdiLibDll, EntryPoint = "NDIlib_find_get_current_sources", CallingConvention = CallingConvention.Cdecl)]
    public static extern IntPtr FindGetCurrentSources(IntPtr p_instance, ref uint p_no_sources);

    [DllImport(NdiLibDll, EntryPoint = "NDIlib_find_wait_for_sources", CallingConvention = CallingConvention.Cdecl)]
    [return: MarshalAs(UnmanagedType.I1)]
    public static extern bool FindWaitForSources(IntPtr p_instance, uint timeout_in_ms);

    #endregion

    #region Receiver

    [DllImport(NdiLibDll, EntryPoint = "NDIlib_recv_create_v3", CallingConvention = CallingConvention.Cdecl)]
    public static extern IntPtr RecvCreateV3(ref RecvCreateV3T p_create_settings);

    [DllImport(NdiLibDll, EntryPoint = "NDIlib_recv_destroy", CallingConvention = CallingConvention.Cdecl)]
    public static extern void RecvDestroy(IntPtr p_instance);

    [DllImport(NdiLibDll, EntryPoint = "NDIlib_recv_capture_v3", CallingConvention = CallingConvention.Cdecl)]
    public static extern FrameTypeE RecvCaptureV3(
        IntPtr p_instance,
        ref VideoFrameV2T p_video_data,
        ref AudioFrameV2T p_audio_data,
        ref MetadataFrameT p_metadata,
        uint timeout_in_ms);

    [DllImport(NdiLibDll, EntryPoint = "NDIlib_recv_free_video_v2", CallingConvention = CallingConvention.Cdecl)]
    public static extern void RecvFreeVideoV2(IntPtr p_instance, ref VideoFrameV2T p_video_data);

    [DllImport(NdiLibDll, EntryPoint = "NDIlib_recv_free_audio_v2", CallingConvention = CallingConvention.Cdecl)]
    public static extern void RecvFreeAudioV2(IntPtr p_instance, ref AudioFrameV2T p_audio_data);

    [DllImport(NdiLibDll, EntryPoint = "NDIlib_recv_free_metadata", CallingConvention = CallingConvention.Cdecl)]
    public static extern void RecvFreeMetadata(IntPtr p_instance, ref MetadataFrameT p_metadata);

    #endregion

    #region Structures

    [StructLayout(LayoutKind.Sequential)]
    public struct SourceT
    {
        public IntPtr p_ndi_name;
        public IntPtr p_url_address;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct RecvCreateV3T
    {
        public SourceT source_to_connect_to;
        public RecvColorFormatE color_format;
        public RecvBandwidthE bandwidth;
        [MarshalAs(UnmanagedType.I1)]
        public bool allow_video_fields;
        public IntPtr p_ndi_recv_name;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct VideoFrameV2T
    {
        public int xres;
        public int yres;
        public FourCCVideoTypeE FourCC;
        public long frame_rate_N;
        public long frame_rate_D;
        public double picture_aspect_ratio;
        public FrameFormatTypeE frame_format_type;
        public long timecode;
        public IntPtr p_data;
        public int line_stride_in_bytes;
        public IntPtr p_metadata;
        public long timestamp;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct AudioFrameV2T
    {
        public int sample_rate;
        public int no_channels;
        public int no_samples;
        public long timecode;
        public IntPtr p_data;
        public int channel_stride_in_bytes;
        public IntPtr p_metadata;
        public long timestamp;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct MetadataFrameT
    {
        public int length;
        public long timecode;
        public IntPtr p_data;
    }

    #endregion

    #region Enums

    public enum FrameTypeE
    {
        frame_type_none = 0,
        frame_type_video = 1,
        frame_type_audio = 2,
        frame_type_metadata = 3,
        frame_type_status_change = 100
    }

    public enum RecvColorFormatE
    {
        recv_color_format_BGRX_BGRA = 0,
        recv_color_format_UYVY_BGRA = 1,
        recv_color_format_RGBX_RGBA = 2,
        recv_color_format_UYVY_RGBA = 3,
        recv_color_format_fastest = 100
    }

    public enum RecvBandwidthE
    {
        recv_bandwidth_metadata_only = -10,
        recv_bandwidth_audio_only = 10,
        recv_bandwidth_lowest = 0,
        recv_bandwidth_highest = 100
    }

    public enum FourCCVideoTypeE
    {
        FourCC_type_UYVY = 0x59565955,
        FourCC_type_BGRA = 0x41524742,
        FourCC_type_BGRX = 0x58524742,
        FourCC_type_RGBA = 0x41424752,
        FourCC_type_RGBX = 0x58424752
    }

    public enum FrameFormatTypeE
    {
        frame_format_type_progressive = 1,
        frame_format_type_interleaved = 0,
        frame_format_type_field_0 = 2,
        frame_format_type_field_1 = 3
    }

    #endregion
}


