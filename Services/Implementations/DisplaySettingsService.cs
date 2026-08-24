using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Threading.Tasks;
using ChyguiSlide.Services.Abstractions;
using ChyguiSlide.Services.Models;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Windowing;
using Windows.Storage;
using Windows.Graphics;
using System;

namespace ChyguiSlide.Services.Implementations;

internal static class Win32DisplayApi
{
    [DllImport("user32.dll")]
    public static extern bool EnumDisplayMonitors(IntPtr hdc, IntPtr lprcClip, MonitorEnumProc lpfnEnum, IntPtr dwData);

    [DllImport("user32.dll")]
    public static extern bool GetMonitorInfo(IntPtr hMonitor, ref MonitorInfo lpmi);

    public delegate bool MonitorEnumProc(IntPtr hMonitor, IntPtr hdcMonitor, ref Rect lprcMonitor, IntPtr dwData);

    [StructLayout(LayoutKind.Sequential)]
    public struct Rect
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

    // Базовая структура MONITORINFO без DeviceName (40 байт)
    [StructLayout(LayoutKind.Sequential)]
    public struct MonitorInfo
    {
        public int Size;
        public Rect Monitor;
        public Rect WorkArea;
        public uint Flags;
    }

    public const int MONITORINFOF_PRIMARY = 0x00000001;
}

// Класс для хранения данных callback
internal class MonitorEnumData
{
    public List<(Win32DisplayApi.Rect rect, bool isPrimary, IntPtr handle)> Monitors { get; } = new();
}

// Статический класс для callback функции
internal static class MonitorEnumHelper
{
    private static readonly object _lock = new object();
    private static MonitorEnumData? _currentData;
    private static int _callbackCallCount = 0;
    
    public static bool MonitorEnumCallback(IntPtr hMonitor, IntPtr hdcMonitor, ref Win32DisplayApi.Rect lprcMonitor, IntPtr dwData)
    {
        _callbackCallCount++;
        System.Diagnostics.Debug.WriteLine($"=== MonitorEnumCallback вызван #{_callbackCallCount}, hMonitor={hMonitor}, Rect: {lprcMonitor.Left},{lprcMonitor.Top}-{lprcMonitor.Right},{lprcMonitor.Bottom} ===");
        
        try
        {
            MonitorEnumData? data;
            lock (_lock)
            {
                data = _currentData;
            }
            
            if (data == null)
            {
                System.Diagnostics.Debug.WriteLine($"MonitorEnumCallback #{_callbackCallCount}: data is null! Пропускаем монитор.");
                return true; // Продолжаем перечисление
            }
            
            // Пытаемся получить информацию о мониторе
            var mi = new Win32DisplayApi.MonitorInfo
            {
                Size = Marshal.SizeOf(typeof(Win32DisplayApi.MonitorInfo))
            };
            
            bool isPrimary = false;
            bool gotInfo = Win32DisplayApi.GetMonitorInfo(hMonitor, ref mi);
            
            if (gotInfo)
            {
                isPrimary = (mi.Flags & Win32DisplayApi.MONITORINFOF_PRIMARY) != 0;
                System.Diagnostics.Debug.WriteLine($"✓ GetMonitorInfo успешно: Primary={isPrimary}");
            }
            else
            {
                // Если GetMonitorInfo не сработал, используем координаты из callback
                // Считаем монитор основным, если он начинается с (0,0)
                isPrimary = (lprcMonitor.Left == 0 && lprcMonitor.Top == 0);
                System.Diagnostics.Debug.WriteLine($"⚠ GetMonitorInfo failed, используем координаты из callback. Primary={isPrimary} (по координатам)");
            }
            
            lock (data.Monitors)
            {
                data.Monitors.Add((lprcMonitor, isPrimary, hMonitor));
                var count = data.Monitors.Count;
                System.Diagnostics.Debug.WriteLine($"✓ Монитор #{count} добавлен: {lprcMonitor.Left},{lprcMonitor.Top} - {lprcMonitor.Right},{lprcMonitor.Bottom}, Primary={isPrimary}, Handle={hMonitor}");
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"✗ Ошибка в MonitorEnumCallback #{_callbackCallCount}: {ex.Message}, StackTrace: {ex.StackTrace}");
        }
        return true; // Всегда возвращаем true, чтобы продолжить перечисление
    }
    
    public static void SetCurrentData(MonitorEnumData data)
    {
        lock (_lock)
        {
            _currentData = data;
            _callbackCallCount = 0;
            System.Diagnostics.Debug.WriteLine("SetCurrentData: установлен новый список для перечисления мониторов");
        }
    }
    
    public static void ClearCurrentData()
    {
        lock (_lock)
        {
            System.Diagnostics.Debug.WriteLine($"ClearCurrentData: callback был вызван {_callbackCallCount} раз(а)");
            _currentData = null;
        }
    }
}

public sealed class DisplaySettingsService : IDisplaySettingsService
{
    private const string SelectedDisplayIdKey = "SelectedDisplayId";
    private const string SelectedThemePresetIdKey = "SelectedThemePresetId";
    private const string WordWrapKey = "WordWrap";
    private const string TextLayoutModeKey = "TextLayoutMode";
    private const string CameraHostKey = "CameraHost";
    private const string CameraPortKey = "CameraPort";
    private const string NdiSourceNameKey = "NdiSourceName";
    private const string HotkeyKeyPrefix = "Hotkey_";
    private const string SettingsFileName = "display-settings.json";
    private readonly DispatcherQueue _dispatcher;
    private readonly string _settingsFilePath;
    
    public DisplaySettingsService()
    {
        _dispatcher = DispatcherQueue.GetForCurrentThread() ?? App.MainDispatcherQueue;
        
        // Используем локальный каталог приложения в %LocalAppData%
        var root = ChyguiSlide.Data.AppPaths.GetLocalAppDataRoot();
        _settingsFilePath = Path.Combine(root, SettingsFileName);
    }

    public async Task<IReadOnlyList<DisplayInfo>> GetAvailableDisplaysAsync()
    {
        var displays = new List<DisplayInfo>();
        var enumData = new MonitorEnumData();
        
        try
        {
            // Устанавливаем статический список - используем только его для простоты
            MonitorEnumHelper.SetCurrentData(enumData);
            
            // Используем Win32 API для получения всех дисплеев
            Win32DisplayApi.MonitorEnumProc enumProc = MonitorEnumHelper.MonitorEnumCallback;

            // Перечисляем все мониторы через Win32 API (синхронно, так как это Win32 вызов)
            try
            {
                System.Diagnostics.Debug.WriteLine("=== НАЧАЛО: Вызываем EnumDisplayMonitors ===");
                // Передаем IntPtr.Zero, так как используем статический список
                var result = Win32DisplayApi.EnumDisplayMonitors(IntPtr.Zero, IntPtr.Zero, enumProc, IntPtr.Zero);
                System.Diagnostics.Debug.WriteLine($"=== КОНЕЦ: EnumDisplayMonitors вернул {result}, найдено мониторов: {enumData.Monitors.Count} ===");
                
                if (!result)
                {
                    var error = Marshal.GetLastWin32Error();
                    System.Diagnostics.Debug.WriteLine($"✗ EnumDisplayMonitors failed, error: {error}");
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"✗ Ошибка EnumDisplayMonitors: {ex.Message}, StackTrace: {ex.StackTrace}");
            }
            finally
            {
                MonitorEnumHelper.ClearCurrentData();
            }

            var monitorList = enumData.Monitors;
            System.Diagnostics.Debug.WriteLine($"=== ИТОГО: Найдено мониторов через Win32 API: {monitorList.Count} ===");
            
            // Если получили мониторы через Win32 API, используем их напрямую
            if (monitorList.Count > 0)
            {
                int index = 1;
                foreach (var (monitorRect, isPrimary, handle) in monitorList)
                {
                    try
                    {
                        // Используем полный размер монитора
                        var width = monitorRect.Right - monitorRect.Left;
                        var height = monitorRect.Bottom - monitorRect.Top;
                        
                        // Используем уникальный ID на основе координат, размера и handle
                        // Это будет стабильный ID, который можно использовать для выбора экрана
                        var displayId = $"monitor_{monitorRect.Left}_{monitorRect.Top}_{width}_{height}_{handle}";
                        
                        displays.Add(new DisplayInfo
                        {
                            Id = displayId,
                            Name = $"Экран {index} {(isPrimary ? "(Основной)" : "")} - {width}x{height}",
                            Width = width,
                            Height = height,
                            X = monitorRect.Left,
                            Y = monitorRect.Top,
                            IsPrimary = isPrimary
                        });
                        index++;
                    }
                    catch (Exception ex)
                    {
                        System.Diagnostics.Debug.WriteLine($"Ошибка обработки монитора: {ex.Message}, StackTrace: {ex.StackTrace}");
                        continue;
                    }
                }
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Общая ошибка в GetAvailableDisplaysAsync: {ex.Message}");
        }

        // Если не получили мониторы через Win32 API или их недостаточно, используем DisplayArea как fallback
        if (displays.Count == 0)
        {
            System.Diagnostics.Debug.WriteLine("Используем DisplayArea.FindAll() как fallback");
            try
            {
                var displayAreas = DisplayArea.FindAll();
                System.Diagnostics.Debug.WriteLine($"DisplayArea.FindAll() вернул {displayAreas.Count} дисплеев");

                int index = 1;
                foreach (var displayArea in displayAreas)
                {
                    try
                    {
                        var workArea = displayArea.WorkArea;
                        var isPrimary = workArea.X == 0 && workArea.Y == 0;
                        
                        string displayId;
                        try
                        {
                            displayId = displayArea.DisplayId.Value.ToString();
                        }
                        catch
                        {
                            // Если DisplayId невалиден, используем координаты
                            displayId = $"displayarea_{workArea.X}_{workArea.Y}_{workArea.Width}_{workArea.Height}";
                        }
                        
                        displays.Add(new DisplayInfo
                        {
                            Id = displayId,
                            Name = $"Экран {index} {(isPrimary ? "(Основной)" : "")} - {workArea.Width}x{workArea.Height}",
                            Width = workArea.Width,
                            Height = workArea.Height,
                            X = workArea.X,
                            Y = workArea.Y,
                            IsPrimary = isPrimary
                        });
                        index++;
                    }
                    catch (Exception ex)
                    {
                        System.Diagnostics.Debug.WriteLine($"Ошибка обработки DisplayArea: {ex.Message}, StackTrace: {ex.StackTrace}");
                        continue;
                    }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Ошибка DisplayArea.FindAll(): {ex.Message}, StackTrace: {ex.StackTrace}");
            }
        }
        
        System.Diagnostics.Debug.WriteLine($"Итого дисплеев в списке: {displays.Count}");

        // Если все равно нет дисплеев, создаем дефолтный
        if (displays.Count == 0)
        {
            displays.Add(new DisplayInfo
            {
                Id = "0",
                Name = "Основной экран",
                Width = 1920,
                Height = 1080,
                X = 0,
                Y = 0,
                IsPrimary = true
            });
        }

        return displays.OrderByDescending(d => d.IsPrimary).ThenBy(d => d.X).ThenBy(d => d.Y).ToList();
    }

    public async Task<string?> GetSelectedDisplayIdAsync()
    {
        try
        {
            if (_dispatcher.HasThreadAccess)
            {
                return GetSelectedDisplayIdSync();
            }
            
            var tcs = new TaskCompletionSource<string?>();
            _dispatcher.TryEnqueue(() =>
            {
                try
                {
                    var result = GetSelectedDisplayIdSync();
                    tcs.SetResult(result);
                }
                catch (Exception ex)
                {
                    tcs.SetException(ex);
                }
            });
            
            return await tcs.Task;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"GetSelectedDisplayIdAsync: Критическая ошибка: {ex.Message}, StackTrace: {ex.StackTrace}");
            return null;
        }
    }
    
    private string? GetSelectedDisplayIdSync()
    {
        try
        {
            if (!File.Exists(_settingsFilePath))
            {
                System.Diagnostics.Debug.WriteLine($"GetSelectedDisplayIdAsync: Файл настроек не существует: '{_settingsFilePath}'");
                return null;
            }
            
            var json = File.ReadAllText(_settingsFilePath);
            if (string.IsNullOrWhiteSpace(json))
            {
                System.Diagnostics.Debug.WriteLine($"GetSelectedDisplayIdAsync: Файл настроек пуст");
                return null;
            }
            
            var settings = JsonSerializer.Deserialize<Dictionary<string, string>>(json);
            if (settings is not null && settings.TryGetValue(SelectedDisplayIdKey, out var displayId) && !string.IsNullOrEmpty(displayId))
            {
                System.Diagnostics.Debug.WriteLine($"GetSelectedDisplayIdAsync: Найден сохраненный ID экрана: '{displayId}'");
                return displayId;
            }
            
            System.Diagnostics.Debug.WriteLine($"GetSelectedDisplayIdAsync: Сохраненный ID экрана не найден в настройках");
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"GetSelectedDisplayIdAsync: Ошибка при чтении настроек: {ex.Message}, StackTrace: {ex.StackTrace}");
        }
        return null;
    }

    public async Task SetSelectedDisplayIdAsync(string? displayId)
    {
        try
        {
            if (_dispatcher.HasThreadAccess)
            {
                SetSelectedDisplayIdSync(displayId);
                return;
            }
            
            var tcs = new TaskCompletionSource();
            _dispatcher.TryEnqueue(() =>
            {
                try
                {
                    SetSelectedDisplayIdSync(displayId);
                    tcs.SetResult();
                }
                catch (Exception ex)
                {
                    tcs.SetException(ex);
                }
            });
            
            await tcs.Task;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"SetSelectedDisplayIdAsync: Критическая ошибка: {ex.Message}, StackTrace: {ex.StackTrace}");
        }
    }
    
    private void SetSelectedDisplayIdSync(string? displayId)
    {
        try
        {
            Dictionary<string, string> settings;
            
            // Читаем существующие настройки, если файл есть
            if (File.Exists(_settingsFilePath))
            {
                try
                {
                    var json = File.ReadAllText(_settingsFilePath);
                    settings = JsonSerializer.Deserialize<Dictionary<string, string>>(json) ?? new Dictionary<string, string>();
                }
                catch
                {
                    settings = new Dictionary<string, string>();
                }
            }
            else
            {
                settings = new Dictionary<string, string>();
            }
            
            if (displayId is null || string.IsNullOrEmpty(displayId))
            {
                if (settings.ContainsKey(SelectedDisplayIdKey))
                {
                    settings.Remove(SelectedDisplayIdKey);
                }
                System.Diagnostics.Debug.WriteLine($"SetSelectedDisplayIdAsync: Выбор экрана сброшен (удален из настроек)");
            }
            else
            {
                settings[SelectedDisplayIdKey] = displayId;
                System.Diagnostics.Debug.WriteLine($"SetSelectedDisplayIdAsync: Сохранен ID экрана: '{displayId}'");
            }
            
            // Сохраняем настройки в файл
            var options = new JsonSerializerOptions { WriteIndented = true };
            var jsonToWrite = JsonSerializer.Serialize(settings, options);
            File.WriteAllText(_settingsFilePath, jsonToWrite);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"SetSelectedDisplayIdAsync: Ошибка при сохранении настроек: {ex.Message}, StackTrace: {ex.StackTrace}");
        }
    }

    public async Task<DisplayInfo?> GetSelectedDisplayAsync()
    {
        var displayId = await GetSelectedDisplayIdAsync();
        if (displayId is null)
        {
            System.Diagnostics.Debug.WriteLine($"GetSelectedDisplayAsync: DisplayId = null, возвращаем null");
            return null;
        }

        var displays = await GetAvailableDisplaysAsync();
        System.Diagnostics.Debug.WriteLine($"GetSelectedDisplayAsync: Ищем экран с ID '{displayId}' среди {displays.Count} доступных экранов");
        
        var found = displays.FirstOrDefault(d => d.Id == displayId);
        if (found is null)
        {
            System.Diagnostics.Debug.WriteLine($"GetSelectedDisplayAsync: Экран с ID '{displayId}' не найден. Доступные экраны:");
            foreach (var display in displays)
            {
                System.Diagnostics.Debug.WriteLine($"  - ID: '{display.Id}', Name: '{display.Name}'");
            }
        }
        else
        {
            System.Diagnostics.Debug.WriteLine($"GetSelectedDisplayAsync: Найден экран: ID='{found.Id}', Name='{found.Name}', X={found.X}, Y={found.Y}");
        }
        
        return found;
    }

    public async Task<Guid?> GetSelectedThemePresetIdAsync()
    {
        try
        {
            if (_dispatcher.HasThreadAccess)
            {
                return GetSelectedThemePresetIdSync();
            }
            
            var tcs = new TaskCompletionSource<Guid?>();
            _dispatcher.TryEnqueue(() =>
            {
                try
                {
                    var result = GetSelectedThemePresetIdSync();
                    tcs.SetResult(result);
                }
                catch (Exception ex)
                {
                    tcs.SetException(ex);
                }
            });
            
            return await tcs.Task;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"GetSelectedThemePresetIdAsync: Ошибка: {ex.Message}");
            return null;
        }
    }
    
    private Guid? GetSelectedThemePresetIdSync()
    {
        try
        {
            if (!File.Exists(_settingsFilePath))
            {
                return null;
            }
            
            var json = File.ReadAllText(_settingsFilePath);
            if (string.IsNullOrWhiteSpace(json))
            {
                return null;
            }
            
            var settings = JsonSerializer.Deserialize<Dictionary<string, string>>(json);
            if (settings is not null && settings.TryGetValue(SelectedThemePresetIdKey, out var presetIdStr) && !string.IsNullOrEmpty(presetIdStr))
            {
                if (Guid.TryParse(presetIdStr, out var presetId))
                {
                    return presetId;
                }
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"GetSelectedThemePresetIdSync: Ошибка при чтении настроек: {ex.Message}");
        }
        return null;
    }

    public async Task SetSelectedThemePresetIdAsync(Guid? themePresetId)
    {
        try
        {
            if (_dispatcher.HasThreadAccess)
            {
                SetSelectedThemePresetIdSync(themePresetId);
                return;
            }
            
            var tcs = new TaskCompletionSource();
            _dispatcher.TryEnqueue(() =>
            {
                try
                {
                    SetSelectedThemePresetIdSync(themePresetId);
                    tcs.SetResult();
                }
                catch (Exception ex)
                {
                    tcs.SetException(ex);
                }
            });
            
            await tcs.Task;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"SetSelectedThemePresetIdAsync: Ошибка: {ex.Message}");
        }
    }
    
    private void SetSelectedThemePresetIdSync(Guid? themePresetId)
    {
        try
        {
            Dictionary<string, string> settings;
            
            // Читаем существующие настройки, если файл есть
            if (File.Exists(_settingsFilePath))
            {
                try
                {
                    var json = File.ReadAllText(_settingsFilePath);
                    settings = JsonSerializer.Deserialize<Dictionary<string, string>>(json) ?? new Dictionary<string, string>();
                }
                catch
                {
                    settings = new Dictionary<string, string>();
                }
            }
            else
            {
                settings = new Dictionary<string, string>();
            }
            
            if (themePresetId is null)
            {
                if (settings.ContainsKey(SelectedThemePresetIdKey))
                {
                    settings.Remove(SelectedThemePresetIdKey);
                }
            }
            else
            {
                settings[SelectedThemePresetIdKey] = themePresetId.Value.ToString();
            }
            
            // Сохраняем настройки в файл
            var options = new JsonSerializerOptions { WriteIndented = true };
            var jsonToWrite = JsonSerializer.Serialize(settings, options);
            File.WriteAllText(_settingsFilePath, jsonToWrite);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"SetSelectedThemePresetIdSync: Ошибка при сохранении настроек: {ex.Message}");
        }
    }

    public async Task<bool> GetWordWrapAsync()
    {
        try
        {
            if (_dispatcher.HasThreadAccess)
            {
                return GetWordWrapSync();
            }
            
            var tcs = new TaskCompletionSource<bool>();
            _dispatcher.TryEnqueue(() =>
            {
                try
                {
                    var result = GetWordWrapSync();
                    tcs.SetResult(result);
                }
                catch (Exception ex)
                {
                    tcs.SetException(ex);
                }
            });
            
            return await tcs.Task;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"GetWordWrapAsync: Ошибка: {ex.Message}");
            return true; // По умолчанию включен
        }
    }
    
    private bool GetWordWrapSync()
    {
        try
        {
            if (!File.Exists(_settingsFilePath))
            {
                return true; // По умолчанию включен
            }
            
            var json = File.ReadAllText(_settingsFilePath);
            if (string.IsNullOrWhiteSpace(json))
            {
                return true;
            }
            
            var settings = JsonSerializer.Deserialize<Dictionary<string, string>>(json);
            if (settings is not null && settings.TryGetValue(WordWrapKey, out var wordWrapStr) && !string.IsNullOrEmpty(wordWrapStr))
            {
                if (bool.TryParse(wordWrapStr, out var wordWrap))
                {
                    return wordWrap;
                }
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"GetWordWrapSync: Ошибка при чтении настроек: {ex.Message}");
        }
        return true; // По умолчанию включен
    }

    public async Task SetWordWrapAsync(bool wordWrap)
    {
        try
        {
            if (_dispatcher.HasThreadAccess)
            {
                SetWordWrapSync(wordWrap);
                return;
            }
            
            var tcs = new TaskCompletionSource();
            _dispatcher.TryEnqueue(() =>
            {
                try
                {
                    SetWordWrapSync(wordWrap);
                    tcs.SetResult();
                }
                catch (Exception ex)
                {
                    tcs.SetException(ex);
                }
            });
            
            await tcs.Task;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"SetWordWrapAsync: Ошибка: {ex.Message}");
        }
    }
    
    private void SetWordWrapSync(bool wordWrap)
    {
        try
        {
            Dictionary<string, string> settings;
            
            // Читаем существующие настройки, если файл есть
            if (File.Exists(_settingsFilePath))
            {
                try
                {
                    var json = File.ReadAllText(_settingsFilePath);
                    settings = JsonSerializer.Deserialize<Dictionary<string, string>>(json) ?? new Dictionary<string, string>();
                }
                catch
                {
                    settings = new Dictionary<string, string>();
                }
            }
            else
            {
                settings = new Dictionary<string, string>();
            }
            
            settings[WordWrapKey] = wordWrap.ToString();
            settings[TextLayoutModeKey] = (wordWrap ? TextLayoutMode.AutoMaxFit : TextLayoutMode.ShrinkToFit).ToString();
            
            // Сохраняем настройки в файл
            var options = new JsonSerializerOptions { WriteIndented = true };
            var jsonToWrite = JsonSerializer.Serialize(settings, options);
            File.WriteAllText(_settingsFilePath, jsonToWrite);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"SetWordWrapSync: Ошибка при сохранении настроек: {ex.Message}");
        }
    }

    public async Task<TextLayoutMode> GetTextLayoutModeAsync()
    {
        try
        {
            if (!File.Exists(_settingsFilePath))
            {
                return TextLayoutMode.AutoMaxFit;
            }

            var json = File.ReadAllText(_settingsFilePath);
            if (string.IsNullOrWhiteSpace(json))
            {
                return TextLayoutMode.AutoMaxFit;
            }

            var settings = JsonSerializer.Deserialize<Dictionary<string, string>>(json);
            if (settings is null)
            {
                return TextLayoutMode.AutoMaxFit;
            }

            if (settings.TryGetValue(TextLayoutModeKey, out var modeStr)
                && TryNormalizeTextLayoutMode(modeStr, out var mode))
            {
                return mode;
            }

            // Миграция со старого WordWrap
            if (settings.TryGetValue(WordWrapKey, out var wordWrapStr)
                && bool.TryParse(wordWrapStr, out var wordWrap))
            {
                return wordWrap ? TextLayoutMode.AutoMaxFit : TextLayoutMode.ShrinkToFit;
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"GetTextLayoutModeAsync: Ошибка: {ex.Message}");
        }

        return TextLayoutMode.AutoMaxFit;
    }

    public async Task SetTextLayoutModeAsync(TextLayoutMode mode)
    {
        try
        {
            Dictionary<string, string> settings;

            if (File.Exists(_settingsFilePath))
            {
                try
                {
                    var json = File.ReadAllText(_settingsFilePath);
                    settings = JsonSerializer.Deserialize<Dictionary<string, string>>(json) ?? new Dictionary<string, string>();
                }
                catch
                {
                    settings = new Dictionary<string, string>();
                }
            }
            else
            {
                settings = new Dictionary<string, string>();
            }

            settings[TextLayoutModeKey] = mode.ToString();
            settings[WordWrapKey] = (mode != TextLayoutMode.ShrinkToFit).ToString();

            var options = new JsonSerializerOptions { WriteIndented = true };
            File.WriteAllText(_settingsFilePath, JsonSerializer.Serialize(settings, options));
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"SetTextLayoutModeAsync: Ошибка: {ex.Message}");
        }

        await Task.CompletedTask;
    }

    public async Task<string?> GetCameraHostAsync()
    {
        try
        {
            if (!File.Exists(_settingsFilePath))
            {
                return null;
            }
            
            var json = File.ReadAllText(_settingsFilePath);
            if (string.IsNullOrWhiteSpace(json))
            {
                return null;
            }
            
            var settings = JsonSerializer.Deserialize<Dictionary<string, string>>(json);
            if (settings is not null && settings.TryGetValue(CameraHostKey, out var host) && !string.IsNullOrEmpty(host))
            {
                return host;
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"GetCameraHostAsync: Ошибка: {ex.Message}");
        }
        return null;
    }

    public async Task SetCameraHostAsync(string? host)
    {
        try
        {
            Dictionary<string, string> settings;
            
            if (File.Exists(_settingsFilePath))
            {
                try
                {
                    var json = File.ReadAllText(_settingsFilePath);
                    settings = JsonSerializer.Deserialize<Dictionary<string, string>>(json) ?? new Dictionary<string, string>();
                }
                catch
                {
                    settings = new Dictionary<string, string>();
                }
            }
            else
            {
                settings = new Dictionary<string, string>();
            }
            
            if (host is null || string.IsNullOrEmpty(host))
            {
                settings.Remove(CameraHostKey);
            }
            else
            {
                settings[CameraHostKey] = host;
            }
            
            var options = new JsonSerializerOptions { WriteIndented = true };
            var jsonToWrite = JsonSerializer.Serialize(settings, options);
            File.WriteAllText(_settingsFilePath, jsonToWrite);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"SetCameraHostAsync: Ошибка: {ex.Message}");
        }
        await Task.CompletedTask;
    }

    public async Task<int> GetCameraPortAsync()
    {
        try
        {
            if (!File.Exists(_settingsFilePath))
            {
                return 5000; // По умолчанию порт 5000
            }
            
            var json = File.ReadAllText(_settingsFilePath);
            if (string.IsNullOrWhiteSpace(json))
            {
                return 5000;
            }
            
            var settings = JsonSerializer.Deserialize<Dictionary<string, string>>(json);
            if (settings is not null && settings.TryGetValue(CameraPortKey, out var portStr) && 
                int.TryParse(portStr, out var port) && port > 0 && port <= 65535)
            {
                return port;
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"GetCameraPortAsync: Ошибка: {ex.Message}");
        }
        return 5000; // По умолчанию порт 5000
    }

    public async Task SetCameraPortAsync(int port)
    {
        try
        {
            Dictionary<string, string> settings;
            
            if (File.Exists(_settingsFilePath))
            {
                try
                {
                    var json = File.ReadAllText(_settingsFilePath);
                    settings = JsonSerializer.Deserialize<Dictionary<string, string>>(json) ?? new Dictionary<string, string>();
                }
                catch
                {
                    settings = new Dictionary<string, string>();
                }
            }
            else
            {
                settings = new Dictionary<string, string>();
            }
            
            if (port > 0 && port <= 65535)
            {
                settings[CameraPortKey] = port.ToString();
            }
            else
            {
                settings[CameraPortKey] = "5000"; // Значение по умолчанию
            }
            
            var options = new JsonSerializerOptions { WriteIndented = true };
            var jsonToWrite = JsonSerializer.Serialize(settings, options);
            File.WriteAllText(_settingsFilePath, jsonToWrite);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"SetCameraPortAsync: Ошибка: {ex.Message}");
        }
        await Task.CompletedTask;
    }

    public async Task<string?> GetNdiSourceNameAsync()
    {
        try
        {
            if (!File.Exists(_settingsFilePath))
            {
                return null;
            }
            
            var json = File.ReadAllText(_settingsFilePath);
            if (string.IsNullOrWhiteSpace(json))
            {
                return null;
            }
            
            var settings = JsonSerializer.Deserialize<Dictionary<string, string>>(json);
            if (settings is not null && settings.TryGetValue(NdiSourceNameKey, out var sourceName) && !string.IsNullOrEmpty(sourceName))
            {
                return sourceName;
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"GetNdiSourceNameAsync: Ошибка: {ex.Message}");
        }
        return null;
    }

    public async Task SetNdiSourceNameAsync(string? sourceName)
    {
        try
        {
            Dictionary<string, string> settings;
            
            if (File.Exists(_settingsFilePath))
            {
                try
                {
                    var json = File.ReadAllText(_settingsFilePath);
                    settings = JsonSerializer.Deserialize<Dictionary<string, string>>(json) ?? new Dictionary<string, string>();
                }
                catch
                {
                    settings = new Dictionary<string, string>();
                }
            }
            else
            {
                settings = new Dictionary<string, string>();
            }
            
            if (sourceName is null || string.IsNullOrEmpty(sourceName))
            {
                settings.Remove(NdiSourceNameKey);
            }
            else
            {
                settings[NdiSourceNameKey] = sourceName;
            }
            
            var options = new JsonSerializerOptions { WriteIndented = true };
            var jsonToWrite = JsonSerializer.Serialize(settings, options);
            File.WriteAllText(_settingsFilePath, jsonToWrite);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"SetNdiSourceNameAsync: Ошибка: {ex.Message}");
        }
        await Task.CompletedTask;
    }

    public async Task<HotkeyBinding?> GetHotkeyAsync(AppHotkeyAction action)
    {
        try
        {
            if (!File.Exists(_settingsFilePath))
            {
                return null;
            }

            var json = File.ReadAllText(_settingsFilePath);
            if (string.IsNullOrWhiteSpace(json))
            {
                return null;
            }

            var settings = JsonSerializer.Deserialize<Dictionary<string, string>>(json);
            var key = HotkeyKeyPrefix + action;
            if (settings is not null && settings.TryGetValue(key, out var value))
            {
                return HotkeyBinding.TryParse(value);
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"GetHotkeyAsync: Ошибка: {ex.Message}");
        }

        return null;
    }

    public async Task SetHotkeyAsync(AppHotkeyAction action, HotkeyBinding? binding)
    {
        try
        {
            Dictionary<string, string> settings;

            if (File.Exists(_settingsFilePath))
            {
                try
                {
                    var json = File.ReadAllText(_settingsFilePath);
                    settings = JsonSerializer.Deserialize<Dictionary<string, string>>(json) ?? new Dictionary<string, string>();
                }
                catch
                {
                    settings = new Dictionary<string, string>();
                }
            }
            else
            {
                settings = new Dictionary<string, string>();
            }

            var key = HotkeyKeyPrefix + action;
            if (binding is null || binding.Key == global::Windows.System.VirtualKey.None)
            {
                settings[key] = HotkeyBinding.Create(global::Windows.System.VirtualKey.None).Serialize();
            }
            else
            {
                settings[key] = binding.Serialize();
            }

            var options = new JsonSerializerOptions { WriteIndented = true };
            File.WriteAllText(_settingsFilePath, JsonSerializer.Serialize(settings, options));
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"SetHotkeyAsync: Ошибка: {ex.Message}");
        }

        await Task.CompletedTask;
    }

    private const string CatalogSortModeKey = "CatalogSortMode";

    public async Task<CatalogSortMode> GetCatalogSortModeAsync()
    {
        try
        {
            if (!File.Exists(_settingsFilePath))
            {
                return CatalogSortMode.Title;
            }

            var json = File.ReadAllText(_settingsFilePath);
            if (string.IsNullOrWhiteSpace(json))
            {
                return CatalogSortMode.Title;
            }

            var settings = JsonSerializer.Deserialize<Dictionary<string, string>>(json);
            if (settings is not null
                && settings.TryGetValue(CatalogSortModeKey, out var modeStr)
                && Enum.TryParse<CatalogSortMode>(modeStr, ignoreCase: true, out var mode))
            {
                return mode;
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"GetCatalogSortModeAsync: {ex.Message}");
        }

        return CatalogSortMode.Title;
    }

    public async Task SetCatalogSortModeAsync(CatalogSortMode mode)
    {
        try
        {
            Dictionary<string, string> settings;
            if (File.Exists(_settingsFilePath))
            {
                try
                {
                    var json = File.ReadAllText(_settingsFilePath);
                    settings = JsonSerializer.Deserialize<Dictionary<string, string>>(json) ?? new Dictionary<string, string>();
                }
                catch
                {
                    settings = new Dictionary<string, string>();
                }
            }
            else
            {
                settings = new Dictionary<string, string>();
            }

            settings[CatalogSortModeKey] = mode.ToString();
            var options = new JsonSerializerOptions { WriteIndented = true };
            File.WriteAllText(_settingsFilePath, JsonSerializer.Serialize(settings, options));
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"SetCatalogSortModeAsync: {ex.Message}");
        }

        await Task.CompletedTask;
    }

    private const string ShowBibleReferenceKey = "ShowBibleReference";
    private const string KeepProjectionBackgroundKey = "KeepProjectionBackground";
    private const string ObsStreamEnabledKey = "ObsStreamEnabled";
    private const string ObsStreamPortKey = "ObsStreamPort";
    private const string ObsStreamBackdropEnabledKey = "ObsStreamBackdropEnabled";
    private const string ObsStreamBackdropOpacityKey = "ObsStreamBackdropOpacity";
    private const string ProjectionMarginLeftKey = "ProjectionMarginLeft";
    private const string ProjectionMarginRightKey = "ProjectionMarginRight";
    private const string ProjectionMarginTopKey = "ProjectionMarginTop";
    private const string ProjectionMarginBottomKey = "ProjectionMarginBottom";
    private const string BibleReferencePlacementKey = "BibleReferencePlacement";
    private const string BibleReferenceAlignmentKey = "BibleReferenceAlignment";

    public async Task<bool> GetShowBibleReferenceAsync()
    {
        var raw = await ReadSettingAsync(ShowBibleReferenceKey).ConfigureAwait(false);
        return bool.TryParse(raw, out var value) && value;
    }

    public async Task SetShowBibleReferenceAsync(bool show)
    {
        await WriteSettingAsync(ShowBibleReferenceKey, show.ToString()).ConfigureAwait(false);
    }

    public async Task<bool> GetKeepProjectionBackgroundAsync()
    {
        var raw = await ReadSettingAsync(KeepProjectionBackgroundKey).ConfigureAwait(false);
        return bool.TryParse(raw, out var value) && value;
    }

    public async Task SetKeepProjectionBackgroundAsync(bool keep)
    {
        System.Diagnostics.Debug.WriteLine($"[DisplaySettingsService] SetKeepProjectionBackgroundAsync: keep={keep}");
        ChyguiSlide.Data.InteractionLogger.Log($"DisplaySettingsService.SetKeepProjectionBackgroundAsync: keep={keep}");
        await WriteSettingAsync(KeepProjectionBackgroundKey, keep.ToString()).ConfigureAwait(false);
        System.Diagnostics.Debug.WriteLine($"[DisplaySettingsService] SetKeepProjectionBackgroundAsync: WriteSettingAsync completed");
        ChyguiSlide.Data.InteractionLogger.Log($"DisplaySettingsService.SetKeepProjectionBackgroundAsync: WriteSettingAsync completed");
    }

    public async Task<bool> CanKeepProjectionBackgroundAsync()
    {
        var displays = await GetAvailableDisplaysAsync().ConfigureAwait(false);
        if (displays.Count < 2)
        {
            return false;
        }

        var selected = await GetSelectedDisplayAsync().ConfigureAwait(false);
        return selected is { IsPrimary: false };
    }

    public async Task<bool> GetObsStreamEnabledAsync()
    {
        var raw = await ReadSettingAsync(ObsStreamEnabledKey).ConfigureAwait(false);
        return bool.TryParse(raw, out var value) && value;
    }

    public async Task SetObsStreamEnabledAsync(bool enabled)
    {
        await WriteSettingAsync(ObsStreamEnabledKey, enabled.ToString()).ConfigureAwait(false);
    }

    public async Task<int> GetObsStreamPortAsync()
    {
        var raw = await ReadSettingAsync(ObsStreamPortKey).ConfigureAwait(false);
        return int.TryParse(raw, out var port) ? Math.Clamp(port, 1024, 65535) : 8765;
    }

    public async Task SetObsStreamPortAsync(int port)
    {
        await WriteSettingAsync(ObsStreamPortKey, Math.Clamp(port, 1024, 65535).ToString()).ConfigureAwait(false);
    }

    public async Task<bool> GetObsStreamBackdropEnabledAsync()
    {
        var raw = await ReadSettingAsync(ObsStreamBackdropEnabledKey).ConfigureAwait(false);
        return bool.TryParse(raw, out var value) && value;
    }

    public async Task SetObsStreamBackdropEnabledAsync(bool enabled)
    {
        await WriteSettingAsync(ObsStreamBackdropEnabledKey, enabled.ToString()).ConfigureAwait(false);
    }

    public async Task<double> GetObsStreamBackdropOpacityAsync()
    {
        var raw = await ReadSettingAsync(ObsStreamBackdropOpacityKey).ConfigureAwait(false);
        if (double.TryParse(raw, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var value))
        {
            return Math.Clamp(value, 0, 1);
        }

        return 0.9;
    }

    public async Task SetObsStreamBackdropOpacityAsync(double opacity)
    {
        var clamped = Math.Clamp(opacity, 0, 1);
        await WriteSettingAsync(
            ObsStreamBackdropOpacityKey,
            clamped.ToString(System.Globalization.CultureInfo.InvariantCulture)).ConfigureAwait(false);
    }

    public Task<int> GetProjectionMarginLeftAsync() => GetProjectionMarginAsync(ProjectionMarginLeftKey, 48);

    public Task SetProjectionMarginLeftAsync(int pixels) =>
        WriteSettingAsync(ProjectionMarginLeftKey, ClampProjectionMargin(pixels).ToString());

    public Task<int> GetProjectionMarginRightAsync() => GetProjectionMarginAsync(ProjectionMarginRightKey, 48);

    public Task SetProjectionMarginRightAsync(int pixels) =>
        WriteSettingAsync(ProjectionMarginRightKey, ClampProjectionMargin(pixels).ToString());

    public Task<int> GetProjectionMarginTopAsync() => GetProjectionMarginAsync(ProjectionMarginTopKey, 40);

    public Task SetProjectionMarginTopAsync(int pixels) =>
        WriteSettingAsync(ProjectionMarginTopKey, ClampProjectionMargin(pixels).ToString());

    public Task<int> GetProjectionMarginBottomAsync() => GetProjectionMarginAsync(ProjectionMarginBottomKey, 40);

    public Task SetProjectionMarginBottomAsync(int pixels) =>
        WriteSettingAsync(ProjectionMarginBottomKey, ClampProjectionMargin(pixels).ToString());

    private async Task<int> GetProjectionMarginAsync(string key, int defaultValue)
    {
        var raw = await ReadSettingAsync(key).ConfigureAwait(false);
        return int.TryParse(raw, out var value) ? ClampProjectionMargin(value) : defaultValue;
    }

    private static int ClampProjectionMargin(int pixels) => Math.Clamp(pixels, 0, 4000);

    public async Task<BibleReferencePlacement> GetBibleReferencePlacementAsync()
    {
        var raw = await ReadSettingAsync(BibleReferencePlacementKey).ConfigureAwait(false);
        return Enum.TryParse<BibleReferencePlacement>(raw, true, out var placement)
            ? placement
            : BibleReferencePlacement.Above;
    }

    public async Task SetBibleReferencePlacementAsync(BibleReferencePlacement placement)
    {
        await WriteSettingAsync(BibleReferencePlacementKey, placement.ToString()).ConfigureAwait(false);
    }

    public async Task<string> GetBibleReferenceAlignmentAsync()
    {
        var raw = await ReadSettingAsync(BibleReferenceAlignmentKey).ConfigureAwait(false);
        return string.IsNullOrWhiteSpace(raw) ? "Center" : raw;
    }

    public async Task SetBibleReferenceAlignmentAsync(string alignment)
    {
        await WriteSettingAsync(BibleReferenceAlignmentKey, alignment ?? "Center").ConfigureAwait(false);
    }

    private const string AppUiThemeKey = "AppUiTheme";

    public async Task<AppUiThemeMode> GetAppUiThemeAsync()
    {
        var raw = await ReadSettingAsync(AppUiThemeKey).ConfigureAwait(false);
        return Enum.TryParse<AppUiThemeMode>(raw, true, out var mode)
            ? mode
            : AppUiThemeMode.System;
    }

    public async Task SetAppUiThemeAsync(AppUiThemeMode mode)
    {
        await WriteSettingAsync(AppUiThemeKey, mode.ToString()).ConfigureAwait(false);
    }

    private const string AskBeforeCloseKey = "AskBeforeClose";

    public async Task<bool> GetAskBeforeCloseAsync()
    {
        var value = await ReadSettingAsync(AskBeforeCloseKey).ConfigureAwait(false);
        return bool.TryParse(value, out var result) ? result : true; // По умолчанию включено
    }

    public async Task SetAskBeforeCloseAsync(bool ask)
    {
        await WriteSettingAsync(AskBeforeCloseKey, ask.ToString()).ConfigureAwait(false);
    }


    private Task<string?> ReadSettingAsync(string key)
    {
        try
        {
            if (!File.Exists(_settingsFilePath))
            {
                return Task.FromResult<string?>(null);
            }

            var json = File.ReadAllText(_settingsFilePath);
            var settings = JsonSerializer.Deserialize<Dictionary<string, string>>(json);
            if (settings is not null && settings.TryGetValue(key, out var value))
            {
                return Task.FromResult<string?>(value);
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"ReadSettingAsync({key}): {ex.Message}");
        }

        return Task.FromResult<string?>(null);
    }

    private Task WriteSettingAsync(string key, string value)
    {
        try
        {
            Dictionary<string, string> settings;
            if (File.Exists(_settingsFilePath))
            {
                try
                {
                    var json = File.ReadAllText(_settingsFilePath);
                    settings = JsonSerializer.Deserialize<Dictionary<string, string>>(json) ?? new Dictionary<string, string>();
                }
                catch
                {
                    settings = new Dictionary<string, string>();
                }
            }
            else
            {
                settings = new Dictionary<string, string>();
            }

            settings[key] = value;
            File.WriteAllText(_settingsFilePath, JsonSerializer.Serialize(settings, new JsonSerializerOptions { WriteIndented = true }));
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"WriteSettingAsync({key}): {ex.Message}");
        }

        return Task.CompletedTask;
    }

    private static bool TryNormalizeTextLayoutMode(string? raw, out TextLayoutMode mode)
    {
        mode = TextLayoutMode.AutoMaxFit;
        if (string.IsNullOrWhiteSpace(raw))
        {
            return false;
        }

        var normalized = raw.Trim();
        if (normalized.Equals("ShrinkToFit", StringComparison.OrdinalIgnoreCase)
            || normalized == "2"
            || normalized == "1")
        {
            mode = TextLayoutMode.ShrinkToFit;
            return true;
        }

        if (normalized.Equals("AutoMaxFit", StringComparison.OrdinalIgnoreCase)
            || normalized.Equals("MaximizeFont", StringComparison.OrdinalIgnoreCase)
            || normalized.Equals("WrapToWidth", StringComparison.OrdinalIgnoreCase)
            || normalized == "0"
            || normalized == "3")
        {
            mode = TextLayoutMode.AutoMaxFit;
            return true;
        }

        return false;
    }
}

