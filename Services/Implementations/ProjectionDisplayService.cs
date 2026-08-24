using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using ChyguiSlide.Controls;
using ChyguiSlide.Data.Entities;
using ChyguiSlide.Services.Abstractions;
using ChyguiSlide.Services.Models;
using ChyguiSlide.ViewModels;
using ChyguiSlide.Windows;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Data;
using Microsoft.UI.Xaml.Controls;
using Windows.Foundation;
using Windows.Graphics;
using Windows.Media.Core;
using Windows.Media.Playback;
using WinRT.Interop;

namespace ChyguiSlide.Services.Implementations;

public sealed class ProjectionDisplayService : IProjectionDisplayService
{
    private readonly IServiceProvider _serviceProvider;
    private readonly IDisplaySettingsService _displaySettingsService;
    private readonly ICatalogService _catalogService;
    private readonly ICameraStreamService _cameraStreamService;
    private readonly INdiReceiverService? _ndiReceiverService;
    private readonly DispatcherQueue _dispatcher;
    private readonly HotkeyDispatcher _hotkeyDispatcher;

    private ProjectionWindowWeb? _windowWeb;
    private ProjectionDisplayViewModel? _viewModel;
    private WebProjectionPreview? _previewStage;
    private Panel? _previewHost;
    private CameraMediaStreamSource? _cameraMediaStreamSource;
    private NdiVideoRenderer? _ndiVideoRenderer;
    private DispatcherQueueTimer? _topMostTimer;
    private IntPtr _projectionHwnd;
    private string? _syncedBackgroundVideoPath;

    private static readonly IntPtr HwndTopMost = new(-1);
    private const uint SwpNomove = 0x0002;
    private const uint SwpNosize = 0x0001;
    private const uint SwpNoactivate = 0x0010;
    private const uint SwpShowwindow = 0x0040;

    private const int DwmwaWindowCornerPreference = 33;
    private const int DwmwaBorderColor = 34;
    private const int DwmwaCaptionColor = 35;
    private const int DwmWcpDoNotRound = 1;

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool SetWindowPos(
        IntPtr hWnd,
        IntPtr hWndInsertAfter,
        int x,
        int y,
        int cx,
        int cy,
        uint uFlags);

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(
        IntPtr hwnd,
        int dwAttribute,
        ref int pvAttribute,
        int cbAttribute);

    public ProjectionDisplayService(
        IServiceProvider serviceProvider, 
        IDisplaySettingsService displaySettingsService, 
        ICatalogService catalogService,
        ICameraStreamService cameraStreamService,
        HotkeyDispatcher hotkeyDispatcher,
        INdiReceiverService? ndiReceiverService = null)
    {
        _serviceProvider = serviceProvider;
        _displaySettingsService = displaySettingsService;
        _catalogService = catalogService;
        _cameraStreamService = cameraStreamService;
        _hotkeyDispatcher = hotkeyDispatcher;
        _ndiReceiverService = ndiReceiverService;
        _dispatcher = App.MainDispatcherQueue;
    }

    public bool IsOpen => _windowWeb is not null;
    public bool IsBlackout => _viewModel?.IsBlackout ?? false;
    public bool IsNdiModeActive => _viewModel?.IsNdiVideoMode ?? false;
    public UIElement? ProgramStage => _previewStage;

    public event EventHandler<bool>? ProjectionWindowVisibilityChanged;
    public event EventHandler<bool>? BlackoutStateChanged;
    public event EventHandler<bool>? NdiModeStateChanged;

    public async Task ShowAsync()
    {
        await RunOnDispatcherAsync(async () =>
        {
            ChyguiSlide.Data.InteractionLogger.Log($"ShowAsync: enter. WindowExists={_windowWeb is not null}, PreviewStage={( _previewStage is null ? "null" : _previewStage.GetHashCode().ToString())}, PreviewHost={( _previewHost is null ? "null" : _previewHost.GetHashCode().ToString())}");
            System.Diagnostics.Debug.WriteLine($"[ProjectionDisplay] ShowAsync enter. PreviewHost={( _previewHost is null ? "null" : _previewHost.GetHashCode().ToString())}");
            if (_windowWeb is not null)
            {
                return;
            }

            await ShowWebWindowAsync();
        });
    }

    private async Task ShowWebWindowAsync()
    {
        _viewModel ??= _serviceProvider.GetRequiredService<ProjectionDisplayViewModel>();
        _windowWeb = ActivatorUtilities.CreateInstance<ProjectionWindowWeb>(_serviceProvider, _viewModel);
        _windowWeb.Closed += OnWindowClosed;

        // Сбрасываем syncedBackgroundVideoPath при открытии нового окна
        var keepBackground = await _displaySettingsService.GetKeepProjectionBackgroundAsync();
        _syncedBackgroundVideoPath = null;

        // Тема и раскладка до Activate
        System.Diagnostics.Debug.WriteLine($"[ProjectionDisplay] ShowWebWindowAsync: Before ApplySavedThemeAsync");
        ChyguiSlide.Data.InteractionLogger.Log($"ShowWebWindowAsync: Before ApplySavedThemeAsync");
        await ApplySavedThemeAsync(startNewBackgroundSession: !keepBackground);
        System.Diagnostics.Debug.WriteLine($"[ProjectionDisplay] ShowWebWindowAsync: After ApplySavedThemeAsync");
        ChyguiSlide.Data.InteractionLogger.Log($"ShowWebWindowAsync: After ApplySavedThemeAsync");
        await ApplyTextLayoutModeAsync();

        // Позиционируем на выбранном экране
        var selectedDisplay = await _displaySettingsService.GetSelectedDisplayAsync();
        DisplayArea? targetDisplayArea = null;

        if (selectedDisplay is not null)
        {
            var displays = await _displaySettingsService.GetAvailableDisplaysAsync();
            var foundDisplay = displays.FirstOrDefault(d => d.Id == selectedDisplay.Id);
            if (foundDisplay is not null)
            {
                var point = new PointInt32(foundDisplay.X, foundDisplay.Y);
                targetDisplayArea = DisplayArea.GetFromPoint(point, DisplayAreaFallback.Nearest);
            }
        }

        _windowWeb.SetFullScreenOnDisplay(targetDisplayArea);
        _windowWeb.Activate();

        // Исключаем окно из Aero Peek
        _windowWeb.ExcludeFromAeroPeek();

        // Хоткеи должны работать и когда фокус на окне проекции
        _hotkeyDispatcher.AttachToProjection(_previewStage);

        // Always-on-top keeper (как у бывшего Win32-окна)
        try
        {
            var hwnd = WindowNative.GetWindowHandle(_windowWeb);
            DisableProjectionWindowChrome(hwnd);
            SetProjectionTopMost(hwnd);
            StartTopMostKeeper(hwnd);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[ProjectionDisplay] TopMost setup failed: {ex.Message}");
        }

        // Возвращаем фокус оператору на главное окно
        App.MainWindow?.Activate();

        // Обновляем превью при открытии трансляции
        if (_previewStage is not null && _previewHost is not null)
        {
            AttachStageToPreview(_previewStage);
            _viewModel?.EnsureContentVisible();
        }

        ProjectionWindowVisibilityChanged?.Invoke(this, true);
    }

    public void BindProgramPreviewHost(Panel? host)
    {
        _ = RunOnDispatcherAsync(async () =>
        {
            ChyguiSlide.Data.InteractionLogger.Log($"BindProgramPreviewHost: called. host={(host is null ? "null" : host.GetHashCode().ToString())}, existingPreviewStage={( _previewStage is null ? "null" : _previewStage.GetHashCode().ToString())}, existingPreviewHost={( _previewHost is null ? "null" : _previewHost.GetHashCode().ToString())}");
            System.Diagnostics.Debug.WriteLine($"[ProjectionDisplay] BindProgramPreviewHost called. host={(host is null ? "null" : host.GetHashCode().ToString())}");
            _previewHost = host;
            
            if (_previewStage is null)
            {
                _viewModel ??= _serviceProvider.GetService<ProjectionDisplayViewModel>() ?? _serviceProvider.GetRequiredService<ProjectionDisplayViewModel>();
                System.Diagnostics.Debug.WriteLine("[ProjectionDisplay] Creating preview stage instance");
                ChyguiSlide.Data.InteractionLogger.Log("Creating preview stage instance");
                _previewStage = new WebProjectionPreview();
                EnsureBackgroundMediaSubscription(_viewModel);
            }

            if (host is not null)
            {
                System.Diagnostics.Debug.WriteLine("[ProjectionDisplay] Attaching preview stage to host");
                ChyguiSlide.Data.InteractionLogger.Log("Attaching preview stage to host");
                AttachStageToPreview(_previewStage);
                _previewStage.BindViewModel(_viewModel!);
                var (width, height) = await ResolveOutputSizeAsync();
                _previewStage.ApplyOutputSize(width, height);
                _viewModel?.EnsureContentVisible();
                _previewStage.SyncNow();
                _ = SyncThemeBackgroundVideoAsync();
            }
            else if (_previewStage?.Parent is Panel parent)
            {
                parent.Children.Remove(_previewStage);
            }
        });
    }

    private async Task<(int Width, int Height)> ResolveOutputSizeAsync()
    {
        try
        {
            var display = await _displaySettingsService.GetSelectedDisplayAsync();
            var width = display?.Width ?? 1920;
            var height = display?.Height ?? 1080;
            if (width < 800 || height < 600)
            {
                return (1920, 1080);
            }

            return (width, height);
        }
        catch
        {
            return (1920, 1080);
        }
    }

    private void AttachStageToPreview(WebProjectionPreview stage)
    {
        if (_previewHost is null)
        {
            return;
        }

        System.Diagnostics.Debug.WriteLine($"[ProjectionDisplay] AttachStageToPreview: stage={(stage is null ? "null" : stage.GetHashCode().ToString())}, previewHost={_previewHost.GetHashCode()}");
        ChyguiSlide.Data.InteractionLogger.Log($"AttachStageToPreview: stage={(stage is null ? "null" : stage.GetHashCode().ToString())}, previewHost={_previewHost.GetHashCode()}");

        if (stage.Parent is Panel current && !ReferenceEquals(current, _previewHost))
        {
            current.Children.Remove(stage);
        }

        if (!ReferenceEquals(stage.Parent, _previewHost))
        {
            _previewHost.Children.Clear();
            _previewHost.Children.Add(stage);
        }
    }

    private async Task ApplySavedThemeAsync(bool startNewBackgroundSession = false)
    {
        try
        {
            var themePresetId = await _displaySettingsService.GetSelectedThemePresetIdAsync();
            System.Diagnostics.Debug.WriteLine($"ApplySavedThemeAsync: themePresetId = {themePresetId}, startNewBackgroundSession = {startNewBackgroundSession}");
            ChyguiSlide.Data.InteractionLogger.Log($"ApplySavedThemeAsync: themePresetId = {themePresetId}, startNewBackgroundSession = {startNewBackgroundSession}");

            if (themePresetId.HasValue)
            {
                // Если настройки уже открыты и тема редактируется/выбрана там,
                // стартуем по текущему состоянию редактора, а не по устаревшей записи из БД.
                if (_serviceProvider.GetService(typeof(ThemePresetEditorViewModel)) is ThemePresetEditorViewModel themeEditor
                    && themeEditor.GetCurrentProjectionTheme() is ThemePreset editorTheme
                    && editorTheme.Id == themePresetId.Value)
                {
                    System.Diagnostics.Debug.WriteLine($"ApplySavedThemeAsync: Применяем стиль из редактора '{editorTheme.Name}'");
                    ChyguiSlide.Data.InteractionLogger.Log($"ApplySavedThemeAsync: Применяем стиль из редактора '{editorTheme.Name}'");
                    ApplyTheme(editorTheme, startNewBackgroundSession);
                    return;
                }

                var themePreset = await _catalogService.GetThemePresetAsync(themePresetId.Value);
                if (themePreset is not null)
                {
                    System.Diagnostics.Debug.WriteLine($"ApplySavedThemeAsync: Применяем стиль из БД '{themePreset.Name}', IsBold={themePreset.IsBold}");
                    ChyguiSlide.Data.InteractionLogger.Log($"ApplySavedThemeAsync: Применяем стиль из БД '{themePreset.Name}'");
                    ApplyTheme(themePreset, startNewBackgroundSession);
                }
                else
                {
                    System.Diagnostics.Debug.WriteLine($"ApplySavedThemeAsync: Стиль с ID {themePresetId.Value} не найден в БД");
                    ChyguiSlide.Data.InteractionLogger.Log($"ApplySavedThemeAsync: Стиль с ID {themePresetId.Value} не найден в БД");
                    ApplyTheme(null, startNewBackgroundSession);
                }
            }
            else
            {
                System.Diagnostics.Debug.WriteLine($"ApplySavedThemeAsync: Стиль не выбран в настройках, применяем null");
                ChyguiSlide.Data.InteractionLogger.Log($"ApplySavedThemeAsync: Стиль не выбран в настройках, применяем null");
                ApplyTheme(null, startNewBackgroundSession);
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"ApplySavedThemeAsync: Ошибка: {ex.Message}");
            ChyguiSlide.Data.InteractionLogger.Log($"ApplySavedThemeAsync: Ошибка: {ex.Message}");
            ApplyTheme(null, startNewBackgroundSession);
        }
    }

    private async Task ApplyTextLayoutModeAsync()
    {
        try
        {
            var mode = await _displaySettingsService.GetTextLayoutModeAsync();
            _viewModel ??= _serviceProvider.GetRequiredService<ProjectionDisplayViewModel>();
            _viewModel.TextLayoutMode = mode;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"ApplyTextLayoutModeAsync: Ошибка: {ex.Message}");
        }
    }

    private async Task ApplyWordWrapAsync()
    {
        await ApplyTextLayoutModeAsync();
    }

    public void Hide()
    {
        if (_windowWeb is null)
        {
            return;
        }

        _ = RunOnDispatcherAsync(async () => await HideWebWindowAsync());
    }

    private async Task HideWebWindowAsync()
    {
        if (_windowWeb is null)
        {
            return;
        }

        var window = _windowWeb;
        StopTopMostKeeper();
        _hotkeyDispatcher.DetachProjection();
        window.Closed -= OnWindowClosed;
        window.DisposeAdapter();

        if (_viewModel is not null)
        {
            _viewModel.BackgroundMediaChanged -= OnBackgroundMediaChanged;
        }

        _syncedBackgroundVideoPath = null;
        _windowWeb = null;
        // Не обнуляем _viewModel и _previewStage — они используются для превью
        window.Close();
        ProjectionWindowVisibilityChanged?.Invoke(this, false);

        try
        {
            await DisconnectFromCameraAsync();
            await DisconnectFromNdiAsync();
            await TeardownNdiVideoAsync();
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[ProjectionDisplay] HideWebWindowAsync teardown: {ex.Message}");
        }
    }

    public void SetBlackout(bool isBlackout)
    {
        _ = RunOnDispatcherAsync(() =>
        {
            _viewModel ??= _serviceProvider.GetRequiredService<ProjectionDisplayViewModel>();
            _viewModel.SetBlackout(isBlackout);
            BlackoutStateChanged?.Invoke(this, _viewModel.IsBlackout);
        });
    }

    public void EnsureContentVisible()
    {
        _viewModel?.EnsureContentVisible();
        
        // Принудительно обновляем превью при изменении контента
        if (_previewStage is not null && _previewHost is not null)
        {
            AttachStageToPreview(_previewStage);
        }
    }

    public void ApplyTheme(ThemePreset? theme) => ApplyTheme(theme, startNewBackgroundSession: false);

    private void ApplyTheme(ThemePreset? theme, bool startNewBackgroundSession)
    {
        _ = RunOnDispatcherAsync(async () =>
        {
            try
            {
                _viewModel ??= _serviceProvider.GetRequiredService<ProjectionDisplayViewModel>();
                EnsureBackgroundMediaSubscription(_viewModel);
                _viewModel.ApplyTheme(theme, startNewBackgroundSession);
                await SyncThemeBackgroundVideoAsync();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"ApplyTheme error: {ex.Message}");
                ChyguiSlide.Data.InteractionLogger.Log($"ApplyTheme error: {ex.Message}");
            }
        });
    }

    private void EnsureBackgroundMediaSubscription(ProjectionDisplayViewModel viewModel)
    {
        viewModel.BackgroundMediaChanged -= OnBackgroundMediaChanged;
        viewModel.BackgroundMediaChanged += OnBackgroundMediaChanged;
    }

    private void OnBackgroundMediaChanged(object? sender, EventArgs e)
    {
        _ = RunOnDispatcherAsync(async () => await SyncThemeBackgroundVideoAsync());
    }

    private async Task SyncThemeBackgroundVideoAsync()
    {
        if (_viewModel is null)
        {
            System.Diagnostics.Debug.WriteLine($"[SyncThemeBackgroundVideoAsync] _viewModel is null, returning");
            ChyguiSlide.Data.InteractionLogger.Log($"SyncThemeBackgroundVideoAsync: _viewModel is null, returning");
            return;
        }

        var players = new MediaPlayerElement?[]
        {
            _previewStage?.BackgroundVideoPlayerElement
        };

        System.Diagnostics.Debug.WriteLine($"[SyncThemeBackgroundVideoAsync] IsBackgroundVideoVisible={_viewModel.IsBackgroundVideoVisible}, BackgroundVideoPath={_viewModel.BackgroundVideoPath}");
        ChyguiSlide.Data.InteractionLogger.Log($"SyncThemeBackgroundVideoAsync: IsBackgroundVideoVisible={_viewModel.IsBackgroundVideoVisible}, BackgroundVideoPath={_viewModel.BackgroundVideoPath}");

        var desiredPath = _viewModel.IsBackgroundVideoVisible
            && !string.IsNullOrWhiteSpace(_viewModel.BackgroundVideoPath)
            && File.Exists(_viewModel.BackgroundVideoPath)
            ? _viewModel.BackgroundVideoPath
            : null;

        System.Diagnostics.Debug.WriteLine($"[SyncThemeBackgroundVideoAsync] desiredPath={desiredPath}, _syncedBackgroundVideoPath={_syncedBackgroundVideoPath}");
        ChyguiSlide.Data.InteractionLogger.Log($"SyncThemeBackgroundVideoAsync: desiredPath={desiredPath}, _syncedBackgroundVideoPath={_syncedBackgroundVideoPath}");

        // Проверяем, есть ли плееры без источника видео (например, только что созданный плеер превью)
        var hasPlayerWithoutSource = players.Any(p => p?.Source is null);
        System.Diagnostics.Debug.WriteLine($"[SyncThemeBackgroundVideoAsync] hasPlayerWithoutSource={hasPlayerWithoutSource}");
        ChyguiSlide.Data.InteractionLogger.Log($"SyncThemeBackgroundVideoAsync: hasPlayerWithoutSource={hasPlayerWithoutSource}");

        // Если путь не изменился, не перезапускаем видео в плеерах, которые уже имеют источник.
        // Но устанавливаем видео в плеерах без источника (превью).
        // Это предотвращает мерцание при первом запуске показа с постоянным фоном,
        // но гарантирует, что превью получит видео.
        var pathUnchanged = string.Equals(_syncedBackgroundVideoPath, desiredPath, StringComparison.OrdinalIgnoreCase);

        System.Diagnostics.Debug.WriteLine($"[SyncThemeBackgroundVideoAsync] pathUnchanged={pathUnchanged}");
        ChyguiSlide.Data.InteractionLogger.Log($"SyncThemeBackgroundVideoAsync: pathUnchanged={pathUnchanged}");

        if (pathUnchanged)
        {
            System.Diagnostics.Debug.WriteLine($"[SyncThemeBackgroundVideoAsync] Path unchanged, setting video only for players without source");
            ChyguiSlide.Data.InteractionLogger.Log($"SyncThemeBackgroundVideoAsync: Path unchanged, setting video only for players without source");

            // Устанавливаем видео только в плеерах без источника
            if (desiredPath is not null)
            {
                foreach (var p in players)
                {
                    if (p?.Source is null && p?.MediaPlayer is not null)
                    {
                        System.Diagnostics.Debug.WriteLine($"[SyncThemeBackgroundVideoAsync] Setting source for player without source");
                        ChyguiSlide.Data.InteractionLogger.Log($"SyncThemeBackgroundVideoAsync: Setting source for player without source");
                        var mediaPlayer = p.MediaPlayer ?? new MediaPlayer();
                        mediaPlayer.IsLoopingEnabled = _viewModel.LoopBackgroundMedia;
                        mediaPlayer.IsMuted = true;
                        mediaPlayer.AutoPlay = true;
                        mediaPlayer.CommandManager.IsEnabled = false;
                        if (p.MediaPlayer is null)
                        {
                            p.SetMediaPlayer(mediaPlayer);
                        }

                        var storageFile = await global::Windows.Storage.StorageFile.GetFileFromPathAsync(desiredPath);
                        p.Source = MediaSource.CreateFromStorageFile(storageFile);
                        mediaPlayer.Play();
                        System.Diagnostics.Debug.WriteLine($"[SyncThemeBackgroundVideoAsync] Source set and play called for player without source");
                        ChyguiSlide.Data.InteractionLogger.Log($"SyncThemeBackgroundVideoAsync: Source set and play called for player without source");
                    }
                    else if (p?.MediaPlayer is not null)
                    {
                        p.MediaPlayer.IsLoopingEnabled = _viewModel.LoopBackgroundMedia;
                    }
                }
            }

            return;
        }

        System.Diagnostics.Debug.WriteLine($"[SyncThemeBackgroundVideoAsync] Path changed, setting new video source");
        ChyguiSlide.Data.InteractionLogger.Log($"SyncThemeBackgroundVideoAsync: Path changed, setting new video source");

        // Очищаем предыдущие источники у всех плееров
        foreach (var p in players)
        {
            if (p is null)
            {
                continue;
            }

            try
            {
                p.Source = null;
                p.MediaPlayer?.Pause();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[ProjectionDisplay] Clear background video (preview/stage/window): {ex.Message}");
            }
        }

        _syncedBackgroundVideoPath = null;

        if (desiredPath is null)
        {
            System.Diagnostics.Debug.WriteLine($"[SyncThemeBackgroundVideoAsync] desiredPath is null, returning");
            ChyguiSlide.Data.InteractionLogger.Log($"SyncThemeBackgroundVideoAsync: desiredPath is null, returning");
            return;
        }

        System.Diagnostics.Debug.WriteLine($"[SyncThemeBackgroundVideoAsync] Setting video source for {players.Length} players");
        ChyguiSlide.Data.InteractionLogger.Log($"SyncThemeBackgroundVideoAsync: Setting video source for {players.Length} players");

        try
        {
            // Устанавливаем источник для каждого плеера отдельно (независимые MediaPlayer)
            foreach (var p in players)
            {
                if (p is null)
                {
                    System.Diagnostics.Debug.WriteLine($"[SyncThemeBackgroundVideoAsync] Player is null, skipping");
                    ChyguiSlide.Data.InteractionLogger.Log($"SyncThemeBackgroundVideoAsync: Player is null, skipping");
                    continue;
                }

                try
                {
                    System.Diagnostics.Debug.WriteLine($"[SyncThemeBackgroundVideoAsync] Setting source for player");
                    ChyguiSlide.Data.InteractionLogger.Log($"SyncThemeBackgroundVideoAsync: Setting source for player");
                    var mediaPlayer = p.MediaPlayer ?? new MediaPlayer();
                    mediaPlayer.IsLoopingEnabled = _viewModel.LoopBackgroundMedia;
                    mediaPlayer.IsMuted = true;
                    mediaPlayer.AutoPlay = true;
                    mediaPlayer.CommandManager.IsEnabled = false;
                    if (p.MediaPlayer is null)
                    {
                        p.SetMediaPlayer(mediaPlayer);
                    }

                    var storageFile = await global::Windows.Storage.StorageFile.GetFileFromPathAsync(desiredPath);
                    p.Source = MediaSource.CreateFromStorageFile(storageFile);
                    mediaPlayer.Play();
                    System.Diagnostics.Debug.WriteLine($"[SyncThemeBackgroundVideoAsync] Source set and play called");
                    ChyguiSlide.Data.InteractionLogger.Log($"SyncThemeBackgroundVideoAsync: Source set and play called");
                }
                catch (Exception exInner)
                {
                    System.Diagnostics.Debug.WriteLine($"[ProjectionDisplay] Background video error for a player: {exInner.Message}");
                }
            }

            _syncedBackgroundVideoPath = desiredPath;
            System.Diagnostics.Debug.WriteLine($"[ProjectionDisplay] Background video set for players: {desiredPath}");
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[ProjectionDisplay] Background video error: {ex.Message}");
        }
    }

    public async Task ToggleVideoModeAsync()
    {
        await RunOnDispatcherAsync(async () =>
        {
            _viewModel ??= _serviceProvider.GetRequiredService<ProjectionDisplayViewModel>();
            _viewModel.IsVideoMode = !_viewModel.IsVideoMode;
            
            if (_viewModel.IsVideoMode)
            {
                // Отключаем NDI, если был включен
                if (_viewModel.IsNdiVideoMode)
                {
                    await DisconnectFromNdiAsync();
                    await TeardownNdiVideoAsync();
                }
                
                // Сначала настраиваем MediaStreamSource, чтобы он был готов принимать config frames
                await SetupVideoStreamAsync();
                // Затем подключаемся к камере и начинаем стриминг
                await ConnectToCameraAsync();
            }
            else
            {
                await DisconnectFromCameraAsync();
                await TeardownVideoStreamAsync();
            }
        });
    }

    public async Task ToggleNdiVideoModeAsync()
    {
        try
        {
            await RunOnDispatcherAsync(async () =>
            {
                try
                {
                    _viewModel ??= _serviceProvider.GetRequiredService<ProjectionDisplayViewModel>();
                    _viewModel.IsNdiVideoMode = !_viewModel.IsNdiVideoMode;
                    
                    // Уведомляем об изменении состояния NDI режима
                    NdiModeStateChanged?.Invoke(this, _viewModel.IsNdiVideoMode);
                    
                    if (_viewModel.IsNdiVideoMode)
                    {
                        // Отключаем камеру, если была включена
                        if (_viewModel.IsVideoMode)
                        {
                            await DisconnectFromCameraAsync();
                            await TeardownVideoStreamAsync();
                        }
                        
                        await SetupNdiVideoAsync();
                        await ConnectToNdiAsync();
                    }
                    else
                    {
                        await DisconnectFromNdiAsync();
                        await TeardownNdiVideoAsync();
                    }
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"[ProjectionDisplay] Error in ToggleNdiVideoModeAsync: {ex.Message}");
                    System.Diagnostics.Debug.WriteLine($"[ProjectionDisplay] StackTrace: {ex.StackTrace}");
                    if (ex.InnerException != null)
                    {
                        System.Diagnostics.Debug.WriteLine($"[ProjectionDisplay] InnerException: {ex.InnerException.Message}");
                    }
                    // Не пробрасываем исключение, чтобы не сломать UI
                }
            });
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[ProjectionDisplay] Outer error in ToggleNdiVideoModeAsync: {ex.Message}");
            System.Diagnostics.Debug.WriteLine($"[ProjectionDisplay] StackTrace: {ex.StackTrace}");
            if (ex.InnerException != null)
            {
                System.Diagnostics.Debug.WriteLine($"[ProjectionDisplay] InnerException: {ex.InnerException.Message}");
            }
        }
    }

    private async Task ConnectToCameraAsync()
    {
        try
        {
            // Если уже подключены, сначала отключаемся
            if (_cameraStreamService.IsConnected)
            {
                System.Diagnostics.Debug.WriteLine("[ProjectionDisplay] Already connected, disconnecting first...");
                await DisconnectFromCameraAsync();
                // Даем время для полного закрытия соединения
                await Task.Delay(200);
            }
            
            var host = await _displaySettingsService.GetCameraHostAsync();
            var port = await _displaySettingsService.GetCameraPortAsync();
            
            System.Diagnostics.Debug.WriteLine($"[ProjectionDisplay] Попытка подключения к камере: host='{host}', port={port}");
            
            if (string.IsNullOrWhiteSpace(host))
            {
                System.Diagnostics.Debug.WriteLine("[ProjectionDisplay] IP адрес камеры не настроен");
                return;
            }

            System.Diagnostics.Debug.WriteLine($"[ProjectionDisplay] Вызываем ConnectAsync({host}, {port})");
            await _cameraStreamService.ConnectAsync(host, port);
            
            System.Diagnostics.Debug.WriteLine($"[ProjectionDisplay] Вызываем StartStreamingAsync()");
            await _cameraStreamService.StartStreamingAsync();
            
            System.Diagnostics.Debug.WriteLine($"[ProjectionDisplay] Подключено к камере {host}:{port}, IsConnected={_cameraStreamService.IsConnected}, IsStreaming={_cameraStreamService.IsStreaming}");
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[ProjectionDisplay] Ошибка подключения к камере: {ex.Message}\n{ex.StackTrace}");
        }
    }

    private async Task DisconnectFromCameraAsync()
    {
        try
        {
            // Проверяем, подключены ли мы, чтобы избежать лишних вызовов
            if (!_cameraStreamService.IsConnected && !_cameraStreamService.IsStreaming)
            {
                System.Diagnostics.Debug.WriteLine("[ProjectionDisplay] Already disconnected, skipping disconnect");
                return;
            }
            
            System.Diagnostics.Debug.WriteLine("[ProjectionDisplay] Disconnecting from camera...");
            await _cameraStreamService.StopStreamingAsync();
            await _cameraStreamService.DisconnectAsync();
            System.Diagnostics.Debug.WriteLine("[ProjectionDisplay] Отключено от камеры");
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[ProjectionDisplay] Ошибка отключения от камеры: {ex.Message}");
        }
    }

    private async Task SetupNdiVideoAsync()
    {
        await RunOnDispatcherAsync(() =>
        {
            try
            {
                System.Diagnostics.Debug.WriteLine("[ProjectionDisplay] Setting up NDI video...");
                
                if (_ndiReceiverService == null)
                {
                    System.Diagnostics.Debug.WriteLine("[ProjectionDisplay] NDI receiver service is not available");
                    return;
                }

                // Создаем NDI video renderer
                _ndiVideoRenderer = new NdiVideoRenderer(_ndiReceiverService);
                _ndiVideoRenderer.BitmapUpdated += OnNdiBitmapUpdated;
                _ndiVideoRenderer.StartRendering();

                System.Diagnostics.Debug.WriteLine("[ProjectionDisplay] NDI video setup complete");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[ProjectionDisplay] Ошибка настройки NDI видео: {ex.Message}\n{ex.StackTrace}");
            }
        });
    }

    private void OnNdiBitmapUpdated(object? sender, Microsoft.UI.Xaml.Media.Imaging.WriteableBitmap bitmap)
    {
        _dispatcher.TryEnqueue(() =>
        {
            try
            {
                if (_previewStage?.NdiVideoImageElement != null)
                {
                    _previewStage.NdiVideoImageElement.Source = bitmap;
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[ProjectionDisplay] Error updating NDI bitmap for preview: {ex.Message}");
            }
        });
    }

    private async Task TeardownNdiVideoAsync()
    {
        await RunOnDispatcherAsync(() =>
        {
            if (_ndiVideoRenderer != null)
            {
                _ndiVideoRenderer.BitmapUpdated -= OnNdiBitmapUpdated;
                _ndiVideoRenderer.StopRendering();
                _ndiVideoRenderer.Dispose();
                _ndiVideoRenderer = null;
            }

            if (_previewStage?.NdiVideoImageElement != null)
            {
                _previewStage.NdiVideoImageElement.Source = null;
            }

            System.Diagnostics.Debug.WriteLine("[ProjectionDisplay] NDI video torn down");
        });
    }

    private async Task ConnectToNdiAsync()
    {
        try
        {
            if (_ndiReceiverService == null)
            {
                System.Diagnostics.Debug.WriteLine("[ProjectionDisplay] NDI receiver service is not available");
                return;
            }

            // Получаем сохраненное имя NDI источника из настроек
            var savedSourceName = await _displaySettingsService.GetNdiSourceNameAsync();
            
            // Получаем список доступных источников
            var sources = await _ndiReceiverService.GetAvailableSourcesAsync();
            if (sources.Count == 0)
            {
                System.Diagnostics.Debug.WriteLine("[ProjectionDisplay] No NDI sources available");
                return;
            }

            string sourceName;
            
            // Если есть сохраненный источник и он доступен, используем его
            if (!string.IsNullOrEmpty(savedSourceName) && sources.Any(s => s.Name == savedSourceName))
            {
                sourceName = savedSourceName;
                System.Diagnostics.Debug.WriteLine($"[ProjectionDisplay] Using saved NDI source: {sourceName}");
            }
            else
            {
                // Иначе используем первый доступный источник
                sourceName = sources[0].Name;
                System.Diagnostics.Debug.WriteLine($"[ProjectionDisplay] Using first available NDI source: {sourceName}");
                
                // Сохраняем выбранный источник
                if (!string.IsNullOrEmpty(sourceName))
                {
                    await _displaySettingsService.SetNdiSourceNameAsync(sourceName);
                }
            }
            
            System.Diagnostics.Debug.WriteLine($"[ProjectionDisplay] Connecting to NDI source: {sourceName}");
            try
            {
                await _ndiReceiverService.ConnectAsync(sourceName);
                System.Diagnostics.Debug.WriteLine($"[ProjectionDisplay] Connected to NDI source: {sourceName}");
            }
            catch (Exception connectEx)
            {
                System.Diagnostics.Debug.WriteLine($"[ProjectionDisplay] Ошибка подключения к NDI источнику: {connectEx.Message}");
                System.Diagnostics.Debug.WriteLine($"[ProjectionDisplay] StackTrace: {connectEx.StackTrace}");
                if (connectEx.InnerException != null)
                {
                    System.Diagnostics.Debug.WriteLine($"[ProjectionDisplay] InnerException: {connectEx.InnerException.Message}");
                }
                // Не пробрасываем исключение дальше, чтобы не сломать UI
                return;
            }
            
            System.Diagnostics.Debug.WriteLine($"[ProjectionDisplay] Starting to receive NDI stream...");
            try
            {
                await _ndiReceiverService.StartReceivingAsync();
                System.Diagnostics.Debug.WriteLine($"[ProjectionDisplay] Started receiving NDI stream");
            }
            catch (Exception receiveEx)
            {
                System.Diagnostics.Debug.WriteLine($"[ProjectionDisplay] Ошибка запуска приема NDI потока: {receiveEx.Message}");
                System.Diagnostics.Debug.WriteLine($"[ProjectionDisplay] StackTrace: {receiveEx.StackTrace}");
                if (receiveEx.InnerException != null)
                {
                    System.Diagnostics.Debug.WriteLine($"[ProjectionDisplay] InnerException: {receiveEx.InnerException.Message}");
                }
                // Не пробрасываем исключение дальше, чтобы не сломать UI
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[ProjectionDisplay] Общая ошибка при подключении к NDI: {ex.Message}\n{ex.StackTrace}");
            if (ex.InnerException != null)
            {
                System.Diagnostics.Debug.WriteLine($"[ProjectionDisplay] InnerException: {ex.InnerException.Message}");
            }
            // Не пробрасываем исключение, чтобы не сломать приложение
        }
    }

    private async Task DisconnectFromNdiAsync()
    {
        try
        {
            if (_ndiReceiverService == null)
            {
                return;
            }

            if (!_ndiReceiverService.IsConnected && !_ndiReceiverService.IsReceiving)
            {
                System.Diagnostics.Debug.WriteLine("[ProjectionDisplay] Already disconnected from NDI");
                return;
            }
            
            System.Diagnostics.Debug.WriteLine("[ProjectionDisplay] Disconnecting from NDI...");
            await _ndiReceiverService.StopReceivingAsync();
            await _ndiReceiverService.DisconnectAsync();
            System.Diagnostics.Debug.WriteLine("[ProjectionDisplay] Disconnected from NDI");
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[ProjectionDisplay] Ошибка отключения от NDI: {ex.Message}");
        }
    }

    public async Task<List<NdiSource>> GetAvailableNdiSourcesAsync()
    {
        try
        {
            if (_ndiReceiverService == null)
            {
                return new List<NdiSource>();
            }

            return await _ndiReceiverService.GetAvailableSourcesAsync();
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[ProjectionDisplay] Ошибка получения списка NDI источников: {ex.Message}");
            return new List<NdiSource>();
        }
    }

    private async Task SetupVideoStreamAsync()
    {
        await RunOnDispatcherAsync(() =>
        {
            try
            {
                System.Diagnostics.Debug.WriteLine("[ProjectionDisplay] Setting up video stream for preview...");
                ChyguiSlide.Data.InteractionLogger.Log("SetupVideoStreamAsync: start - preview MediaPlayer");

                _cameraMediaStreamSource = new CameraMediaStreamSource(_cameraStreamService);
                _cameraMediaStreamSource.MediaStreamSourceChanged += OnMediaStreamSourceChanged;

                var previewVideoPlayer = _previewStage?.VideoPlayerElement;
                if (previewVideoPlayer is null)
                {
                    System.Diagnostics.Debug.WriteLine("[ProjectionDisplay] No preview VideoPlayerElement available");
                    ChyguiSlide.Data.InteractionLogger.Log("SetupVideoStreamAsync: no VideoPlayerElement available");
                    return;
                }

                try
                {
                    var mediaStreamSourcePreview = _cameraMediaStreamSource.CreateMediaStreamSource();
                    var mediaSourcePreview = MediaSource.CreateFromMediaStreamSource(mediaStreamSourcePreview);

                    System.Diagnostics.Debug.WriteLine("[ProjectionDisplay] Setting MediaSource on preview video player...");
                    ChyguiSlide.Data.InteractionLogger.Log("SetupVideoStreamAsync: assigning preview MediaSource");

                    var previewMediaPlayer = previewVideoPlayer.MediaPlayer ?? new MediaPlayer();
                    previewMediaPlayer.IsMuted = false;
                    previewMediaPlayer.AutoPlay = true;
                    previewMediaPlayer.CommandManager.IsEnabled = false;
                    if (previewVideoPlayer.MediaPlayer is null)
                    {
                        previewVideoPlayer.SetMediaPlayer(previewMediaPlayer);
                    }

                    previewVideoPlayer.Source = mediaSourcePreview;
                    previewMediaPlayer.Play();

                    System.Diagnostics.Debug.WriteLine("[ProjectionDisplay] Video stream setup complete");
                    ChyguiSlide.Data.InteractionLogger.Log("SetupVideoStreamAsync: complete");
                }
                catch (Exception innerEx)
                {
                    System.Diagnostics.Debug.WriteLine($"[ProjectionDisplay] Error during SetupVideoStreamAsync assignment: {innerEx.Message}");
                    ChyguiSlide.Data.InteractionLogger.Log($"SetupVideoStreamAsync: error - {innerEx.Message}");
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[ProjectionDisplay] Ошибка настройки видео потока: {ex.Message}\n{ex.StackTrace}");
            }
        });
    }

    private void OnMediaStreamSourceChanged(object? sender, MediaStreamSource newMediaStreamSource)
    {
        _dispatcher.TryEnqueue(async () =>
        {
            try
            {
                System.Diagnostics.Debug.WriteLine("[ProjectionDisplay] MediaStreamSource changed, creating new MediaPlayer");

                var previewVideoPlayer = _previewStage?.VideoPlayerElement;
                if (previewVideoPlayer is null)
                {
                    System.Diagnostics.Debug.WriteLine("[ProjectionDisplay] Preview VideoPlayerElement is null, cannot update MediaSource");
                    return;
                }

                var oldMediaPlayer = previewVideoPlayer.MediaPlayer;
                if (oldMediaPlayer != null)
                {
                    oldMediaPlayer.Pause();
                    oldMediaPlayer.Source = null;
                }

                previewVideoPlayer.Source = null;
                previewVideoPlayer.SetMediaPlayer(null);
                await Task.Delay(500);

                var newMediaPlayer = new MediaPlayer();
                TypedEventHandler<MediaPlayer, object>? mediaOpenedHandler = null;
                mediaOpenedHandler = (s, args) =>
                {
                    newMediaPlayer.MediaOpened -= mediaOpenedHandler;
                    newMediaPlayer.Play();
                };
                newMediaPlayer.MediaOpened += mediaOpenedHandler;

                previewVideoPlayer.SetMediaPlayer(newMediaPlayer);
                await Task.Delay(200);

                var newMediaSource = MediaSource.CreateFromMediaStreamSource(newMediaStreamSource);
                previewVideoPlayer.Source = newMediaSource;
                await Task.Delay(200);
                newMediaPlayer.Play();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[ProjectionDisplay] Ошибка обновления MediaSource: {ex.Message}\n{ex.StackTrace}");
            }
        });
    }

    private async Task TeardownVideoStreamAsync()
    {
        await RunOnDispatcherAsync(() =>
        {
            try
            {
                if (_cameraMediaStreamSource != null)
                {
                    _cameraMediaStreamSource.MediaStreamSourceChanged -= OnMediaStreamSourceChanged;
                }

                var videoPlayer = _previewStage?.VideoPlayerElement;
                if (videoPlayer != null)
                {
                    videoPlayer.Source = null;
                }

                _cameraMediaStreamSource?.Dispose();
                _cameraMediaStreamSource = null;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[ProjectionDisplay] Ошибка остановки видео потока: {ex.Message}");
            }
        });
    }

    private void OnWindowClosed(object sender, WindowEventArgs e)
    {
        if (_windowWeb is null)
        {
            return;
        }

        StopTopMostKeeper();
        _hotkeyDispatcher.DetachProjection();
        if (_viewModel is not null)
        {
            _viewModel.BackgroundMediaChanged -= OnBackgroundMediaChanged;
        }

        _windowWeb.Closed -= OnWindowClosed;
        _windowWeb.DisposeAdapter();
        _syncedBackgroundVideoPath = null;
        _windowWeb = null;
        ProjectionWindowVisibilityChanged?.Invoke(this, false);
    }

    private static void DisableProjectionWindowChrome(IntPtr hwnd)
    {
        if (hwnd == IntPtr.Zero)
        {
            return;
        }

        try
        {
            var corner = DwmWcpDoNotRound;
            _ = DwmSetWindowAttribute(hwnd, DwmwaWindowCornerPreference, ref corner, sizeof(int));

            var black = 0;
            _ = DwmSetWindowAttribute(hwnd, DwmwaBorderColor, ref black, sizeof(int));
            _ = DwmSetWindowAttribute(hwnd, DwmwaCaptionColor, ref black, sizeof(int));
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"DisableProjectionWindowChrome: {ex.Message}");
        }
    }

    private void StartTopMostKeeper(IntPtr hwnd)
    {
        StopTopMostKeeper();
        _projectionHwnd = hwnd;
        _topMostTimer = _dispatcher.CreateTimer();
        _topMostTimer.Interval = TimeSpan.FromSeconds(1);
        _topMostTimer.IsRepeating = true;
        _topMostTimer.Tick += OnTopMostTimerTick;
        _topMostTimer.Start();
    }

    private void OnTopMostTimerTick(DispatcherQueueTimer sender, object args)
    {
        if (_projectionHwnd != IntPtr.Zero && _windowWeb is not null)
        {
            SetProjectionTopMost(_projectionHwnd);
        }
    }

    private void StopTopMostKeeper()
    {
        if (_topMostTimer is not null)
        {
            _topMostTimer.Tick -= OnTopMostTimerTick;
            _topMostTimer.Stop();
            _topMostTimer = null;
        }

        _projectionHwnd = IntPtr.Zero;
    }

    private static void SetProjectionTopMost(IntPtr hwnd)
    {
        if (hwnd == IntPtr.Zero)
        {
            return;
        }

        // Без активации — фокус остаётся у оператора на главном окне
        SetWindowPos(
            hwnd,
            HwndTopMost,
            0,
            0,
            0,
            0,
            SwpNomove | SwpNosize | SwpNoactivate | SwpShowwindow);
    }

    private Task RunOnDispatcherAsync(Func<Task> asyncAction)
    {
        if (_dispatcher.HasThreadAccess)
        {
            try
            {
                return asyncAction();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[ProjectionDisplay] Error in RunOnDispatcherAsync (same thread): {ex.Message}");
                System.Diagnostics.Debug.WriteLine($"[ProjectionDisplay] StackTrace: {ex.StackTrace}");
                return Task.FromException(ex);
            }
        }

        var tcs = new TaskCompletionSource();
        if (!_dispatcher.TryEnqueue(async () =>
            {
                try
                {
                    await asyncAction();
                    tcs.SetResult();
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"[ProjectionDisplay] Error in RunOnDispatcherAsync (dispatched): {ex.Message}");
                    System.Diagnostics.Debug.WriteLine($"[ProjectionDisplay] StackTrace: {ex.StackTrace}");
                    if (ex.InnerException != null)
                    {
                        System.Diagnostics.Debug.WriteLine($"[ProjectionDisplay] InnerException: {ex.InnerException.Message}");
                    }
                    tcs.SetException(ex);
                }
            }))
        {
            tcs.SetException(new InvalidOperationException("Не удалось поставить задачу в очередь диспетчера."));
        }

        return tcs.Task;
    }

    private Task RunOnDispatcherAsync(Action action)
    {
        if (_dispatcher.HasThreadAccess)
        {
            action();
            return Task.CompletedTask;
        }

        var tcs = new TaskCompletionSource();
        if (!_dispatcher.TryEnqueue(() =>
            {
                try
                {
                    action();
                    tcs.SetResult();
                }
                catch (Exception ex)
                {
                    tcs.SetException(ex);
                }
            }))
        {
            tcs.SetException(new InvalidOperationException("Не удалось поставить задачу в очередь диспетчера."));
        }

        return tcs.Task;
    }
}


