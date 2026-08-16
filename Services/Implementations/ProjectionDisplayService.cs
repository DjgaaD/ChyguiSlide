using System;
using System.Collections.Generic;
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

    private ProjectionWindow? _window;
    private ProjectionDisplayViewModel? _viewModel;
    private ProjectionStageView? _stage;
    private ProjectionStageView? _previewStage;
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

    public bool IsOpen => _window is not null;
    public bool IsBlackout => _viewModel?.IsBlackout ?? false;
    public bool IsNdiModeActive => _viewModel?.IsNdiVideoMode ?? false;
    public UIElement? ProgramStage => _stage;

    public event EventHandler<bool>? ProjectionWindowVisibilityChanged;
    public event EventHandler<bool>? BlackoutStateChanged;
    public event EventHandler<bool>? NdiModeStateChanged;

    public async Task ShowAsync()
    {
        await RunOnDispatcherAsync(async () =>
        {
            ChyguiSlide.Data.InteractionLogger.Log($"ShowAsync: enter. WindowExists={_window is not null}, PreviewStage={( _previewStage is null ? "null" : _previewStage.GetHashCode().ToString())}, PreviewHost={( _previewHost is null ? "null" : _previewHost.GetHashCode().ToString())}");
            System.Diagnostics.Debug.WriteLine($"[ProjectionDisplay] ShowAsync enter. PreviewHost={( _previewHost is null ? "null" : _previewHost.GetHashCode().ToString())}");
            if (_window is not null)
            {
                return;
            }

            var stage = EnsureStage();
            _window = ActivatorUtilities.CreateInstance<ProjectionWindow>(_serviceProvider);
            _window.Closed += OnWindowClosed;

            AttachStageToWindow(_window, stage);

            await TrySetFullScreenOnSelectedDisplayAsync(_window);
            _window.Activate();

            // Тема и раскладка после Activate — чтобы COM-компоненты были инициализированы
            _viewModel!.BeginBackgroundSession();
            await ApplySavedThemeAsync(startNewBackgroundSession: true);
            await ApplyTextLayoutModeAsync();

            // Хоткеи должны работать и когда фокус на окне проекции
            _hotkeyDispatcher.AttachToProjection(stage);

            // Возвращаем фокус оператору на главное окно — управление со стрелок/Esc
            App.MainWindow?.Activate();

            // Обновляем превью при открытии трансляции
            if (_previewStage is not null && _previewHost is not null)
            {
                AttachStageToPreview(_previewStage);
                _viewModel?.EnsureContentVisible();
            }

            ProjectionWindowVisibilityChanged?.Invoke(this, true);
        });
    }

    public void BindProgramPreviewHost(Panel? host)
    {
        _ = RunOnDispatcherAsync(() =>
        {
            ChyguiSlide.Data.InteractionLogger.Log($"BindProgramPreviewHost: called. host={(host is null ? "null" : host.GetHashCode().ToString())}, existingPreviewStage={( _previewStage is null ? "null" : _previewStage.GetHashCode().ToString())}, existingPreviewHost={( _previewHost is null ? "null" : _previewHost.GetHashCode().ToString())}");
            System.Diagnostics.Debug.WriteLine($"[ProjectionDisplay] BindProgramPreviewHost called. host={(host is null ? "null" : host.GetHashCode().ToString())}");
            _previewHost = host;
            
            // Создаем отдельный экземпляр для превью, если его нет.
            // Если ViewModel ещё не создана, создаём её здесь, чтобы превью всегда могло быть инициализировано при привязке хоста.
            if (_previewStage is null)
            {
                _viewModel ??= _serviceProvider.GetService<ProjectionDisplayViewModel>() ?? _serviceProvider.GetRequiredService<ProjectionDisplayViewModel>();
                System.Diagnostics.Debug.WriteLine("[ProjectionDisplay] Creating preview stage instance");
                ChyguiSlide.Data.InteractionLogger.Log("Creating preview stage instance");
                _previewStage = new ProjectionStageView();
                _previewStage.BindViewModel(_viewModel, enableTransitionPlayer: false);
            }

            if (host is not null)
            {
                System.Diagnostics.Debug.WriteLine("[ProjectionDisplay] Attaching preview stage to host");
                ChyguiSlide.Data.InteractionLogger.Log("Attaching preview stage to host");
                AttachStageToPreview(_previewStage);
                // Принудительно обновляем превью при привязке
                _viewModel?.EnsureContentVisible();
            }
            else if (_previewStage?.Parent is Panel parent)
            {
                parent.Children.Remove(_previewStage);
            }
        });
    }

    private ProjectionStageView EnsureStage()
    {
        _viewModel ??= _serviceProvider.GetRequiredService<ProjectionDisplayViewModel>();
        if (_stage is null)
        {
            _stage = new ProjectionStageView();
            _stage.BindViewModel(_viewModel);
            EnsureBackgroundMediaSubscription(_viewModel);
            // Тема не применяется здесь - только в ShowAsync для избежания дублирования
            // _ = ApplySavedThemeAsync(startNewBackgroundSession: false);
            _ = ApplyTextLayoutModeAsync();
        }

        return _stage;
    }

    private void AttachStageToWindow(ProjectionWindow window, ProjectionStageView stage)
    {
        if (stage.Parent is Panel previewParent)
        {
            previewParent.Children.Remove(stage);
        }

        window.AttachStage(stage);
    }

    private void AttachStageToPreview(ProjectionStageView stage)
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

    private void ReturnStageToPreview()
    {
        if (_stage is null)
        {
            return;
        }

        if (_window is not null)
        {
            _ = _window.DetachStage();
        }

        AttachStageToPreview(_stage);
    }

    private async Task ApplySavedThemeAsync(bool startNewBackgroundSession = false)
    {
        try
        {
            var themePresetId = await _displaySettingsService.GetSelectedThemePresetIdAsync();
            System.Diagnostics.Debug.WriteLine($"ApplySavedThemeAsync: themePresetId = {themePresetId}");
            
            if (themePresetId.HasValue)
            {
                // Если настройки уже открыты и тема редактируется/выбрана там,
                // стартуем по текущему состоянию редактора, а не по устаревшей записи из БД.
                if (_serviceProvider.GetService(typeof(ThemePresetEditorViewModel)) is ThemePresetEditorViewModel themeEditor
                    && themeEditor.GetCurrentProjectionTheme() is ThemePreset editorTheme
                    && editorTheme.Id == themePresetId.Value)
                {
                    ApplyTheme(editorTheme, startNewBackgroundSession);
                    return;
                }

                var themePreset = await _catalogService.GetThemePresetAsync(themePresetId.Value);
                if (themePreset is not null)
                {
                    System.Diagnostics.Debug.WriteLine($"ApplySavedThemeAsync: Применяем стиль '{themePreset.Name}', IsBold={themePreset.IsBold}");
                    ApplyTheme(themePreset, startNewBackgroundSession);
                }
                else
                {
                    System.Diagnostics.Debug.WriteLine($"ApplySavedThemeAsync: Стиль с ID {themePresetId.Value} не найден");
                    ApplyTheme(null, startNewBackgroundSession);
                }
            }
            else
            {
                System.Diagnostics.Debug.WriteLine($"ApplySavedThemeAsync: Стиль не выбран в настройках");
                ApplyTheme(null, startNewBackgroundSession);
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"ApplySavedThemeAsync: Ошибка: {ex.Message}");
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
        if (_window is null)
        {
            return;
        }

        _ = RunOnDispatcherAsync(async () =>
        {
            if (_window is null)
            {
                return;
            }

            // Сначала закрываем окно (важно при выходе из приложения),
            // затем отключаем камеру/NDI — иначе они могут задержать Close.
            var window = _window;
            StopTopMostKeeper();
            _hotkeyDispatcher.DetachProjection();
            window.Closed -= OnWindowClosed;

            ReturnStageToPreview();

            if (_viewModel is not null)
            {
                _viewModel.BackgroundMediaChanged -= OnBackgroundMediaChanged;
            }

            _syncedBackgroundVideoPath = null;
            _window = null;
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
                System.Diagnostics.Debug.WriteLine($"[ProjectionDisplay] Cleanup after Hide: {ex.Message}");
            }
        });
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
            return;
        }

        var players = new MediaPlayerElement?[]
        {
            _previewStage?.BackgroundVideoPlayerElement,
            _stage?.BackgroundVideoPlayerElement,
            _window?.BackgroundVideoPlayerElement
        };

        var desiredPath = _viewModel.IsBackgroundVideoVisible
            && !string.IsNullOrWhiteSpace(_viewModel.BackgroundVideoPath)
            && File.Exists(_viewModel.BackgroundVideoPath)
            ? _viewModel.BackgroundVideoPath
            : null;

        // Если путь не изменился и у хотя бы одного плеера уже есть Source, просто обновляем флаги.
        if (string.Equals(_syncedBackgroundVideoPath, desiredPath, StringComparison.OrdinalIgnoreCase))
        {
            if (desiredPath is not null)
            {
                foreach (var p in players)
                {
                    if (p?.MediaPlayer is not null)
                    {
                        p.MediaPlayer.IsLoopingEnabled = _viewModel.LoopBackgroundMedia;
                    }
                }
            }

            return;
        }

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
            return;
        }

        try
        {
            // Устанавливаем источник для каждого плеера отдельно (независимые MediaPlayer)
            foreach (var p in players)
            {
                if (p is null)
                {
                    continue;
                }

                try
                {
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
            if (_window == null)
            {
                System.Diagnostics.Debug.WriteLine("[ProjectionDisplay] SetupNdiVideoAsync: Window is null");
                return;
            }

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
            // Обновляем NDI изображение для окна, сцены и превью (если есть)
            try
            {
                if (_window?.NdiVideoImageElement != null)
                {
                    _window.NdiVideoImageElement.Source = bitmap;
                }

                if (_stage?.NdiVideoImageElement != null)
                {
                    _stage.NdiVideoImageElement.Source = bitmap;
                }

                if (_previewStage?.NdiVideoImageElement != null)
                {
                    _previewStage.NdiVideoImageElement.Source = bitmap;
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[ProjectionDisplay] Error updating NDI bitmap for preview/stage/window: {ex.Message}");
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

            if (_window?.NdiVideoImageElement != null)
            {
                _window.NdiVideoImageElement.Source = null;
            }
            else if (_stage?.NdiVideoImageElement != null)
            {
                _stage.NdiVideoImageElement.Source = null;
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
            if (_window == null)
            {
                System.Diagnostics.Debug.WriteLine("[ProjectionDisplay] SetupVideoStreamAsync: Window is null");
                return;
            }

            try
            {
                System.Diagnostics.Debug.WriteLine("[ProjectionDisplay] Setting up video stream (separate players for main and preview)...");
                ChyguiSlide.Data.InteractionLogger.Log("SetupVideoStreamAsync: start - creating separate MediaPlayers/MediaSources for main and preview");

                // Создаем MediaStreamSource для передачи H.264/H.265 данных в MediaPlayer
                _cameraMediaStreamSource = new CameraMediaStreamSource(_cameraStreamService);

                // Подписываемся на событие изменения MediaStreamSource (например, при смене кодека)
                _cameraMediaStreamSource.MediaStreamSourceChanged += OnMediaStreamSourceChanged;

                // Получаем MediaPlayerElement для основного stage/okna и для превью
                var mainVideoPlayer = _stage?.VideoPlayerElement ?? _window?.VideoPlayerElement;
                var previewVideoPlayer = _previewStage?.VideoPlayerElement;

                if (mainVideoPlayer is null && previewVideoPlayer is null)
                {
                    System.Diagnostics.Debug.WriteLine("[ProjectionDisplay] No VideoPlayerElement available for stage/window/preview");
                    ChyguiSlide.Data.InteractionLogger.Log("SetupVideoStreamAsync: no VideoPlayerElement available");
                    return;
                }

                try
                {
                    // Создаем отдельные MediaStreamSource/MediaSource для основного и превью,
                    // чтобы не пытаться подключать один и тот же источник к двум плеерам.
                    var mediaStreamSourceMain = _cameraMediaStreamSource.CreateMediaStreamSource();
                    var mediaStreamSourcePreview = _cameraMediaStreamSource.CreateMediaStreamSource();

                    var mediaSourceMain = MediaSource.CreateFromMediaStreamSource(mediaStreamSourceMain);
                    var mediaSourcePreview = MediaSource.CreateFromMediaStreamSource(mediaStreamSourcePreview);

                    // Настраиваем основной плеер
                    if (mainVideoPlayer is not null)
                    {
                        System.Diagnostics.Debug.WriteLine("[ProjectionDisplay] Setting MediaSource on main video player (separate)...");
                        ChyguiSlide.Data.InteractionLogger.Log("SetupVideoStreamAsync: assigning main MediaSource");

                        var mainMediaPlayer = mainVideoPlayer.MediaPlayer ?? new MediaPlayer();
                        mainMediaPlayer.IsMuted = false;
                        mainMediaPlayer.AutoPlay = true;
                        mainMediaPlayer.CommandManager.IsEnabled = false;
                        if (mainVideoPlayer.MediaPlayer is null)
                        {
                            mainVideoPlayer.SetMediaPlayer(mainMediaPlayer);
                        }

                        mainVideoPlayer.Source = mediaSourceMain;
                        mainMediaPlayer.Play();
                    }

                    // Настраиваем превью плеер
                    if (previewVideoPlayer is not null)
                    {
                        try
                        {
                            System.Diagnostics.Debug.WriteLine("[ProjectionDisplay] Setting MediaSource on preview video player (separate)...");
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
                        }
                        catch (Exception exPreview)
                        {
                            System.Diagnostics.Debug.WriteLine($"[ProjectionDisplay] Failed to set preview MediaSource: {exPreview.Message}");
                            ChyguiSlide.Data.InteractionLogger.Log($"SetupVideoStreamAsync: failed to assign preview MediaSource: {exPreview.Message}");
                        }
                    }

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
                
                var videoPlayer = _stage?.VideoPlayerElement ?? _window?.VideoPlayerElement;
                var previewVideoPlayer = _previewStage?.VideoPlayerElement;

                if (videoPlayer == null && previewVideoPlayer == null)
                {
                    System.Diagnostics.Debug.WriteLine("[ProjectionDisplay] VideoPlayerElement is null, cannot update MediaSource");
                    return;
                }

                // Получаем текущий MediaPlayer и полностью очищаем его (для основного и превью)
                var oldMediaPlayer = videoPlayer?.MediaPlayer;
                if (oldMediaPlayer != null)
                {
                    System.Diagnostics.Debug.WriteLine($"[ProjectionDisplay] Current MediaPlayer state: {oldMediaPlayer.CurrentState}");
                    
                    // Останавливаем и очищаем старый MediaPlayer
                    oldMediaPlayer.Pause();
                    oldMediaPlayer.Source = null;
                    System.Diagnostics.Debug.WriteLine("[ProjectionDisplay] Paused and cleared old MediaPlayer");
                }
                
                // Очищаем Source в MediaPlayerElement (основной и превью)
                if (videoPlayer != null)
                {
                    videoPlayer.Source = null;
                }
                if (previewVideoPlayer != null)
                {
                    previewVideoPlayer.Source = null;
                }
                System.Diagnostics.Debug.WriteLine("[ProjectionDisplay] Cleared MediaPlayerElement Source");
                
                // Полностью удаляем MediaPlayer из MediaPlayerElement
                if (videoPlayer != null)
                {
                    videoPlayer.SetMediaPlayer(null);
                }
                if (previewVideoPlayer != null)
                {
                    previewVideoPlayer.SetMediaPlayer(null);
                }
                System.Diagnostics.Debug.WriteLine("[ProjectionDisplay] Removed MediaPlayer from MediaPlayerElement");
                
                // Задержка для полного освобождения ресурсов
                await Task.Delay(500);
                
                // Создаем новый MediaPlayer явно
                var newMediaPlayer = new MediaPlayer();
                System.Diagnostics.Debug.WriteLine("[ProjectionDisplay] Created new MediaPlayer explicitly");
                
                // Подписываемся на событие MediaOpened для отслеживания готовности
                TypedEventHandler<MediaPlayer, object>? mediaOpenedHandler = null;
                mediaOpenedHandler = (sender, args) =>
                {
                    System.Diagnostics.Debug.WriteLine("[ProjectionDisplay] MediaOpened event fired for new MediaPlayer");
                    if (newMediaPlayer != null)
                    {
                        newMediaPlayer.MediaOpened -= mediaOpenedHandler;
                        newMediaPlayer.Play();
                        System.Diagnostics.Debug.WriteLine("[ProjectionDisplay] Play() called after MediaOpened event");
                    }
                };
                newMediaPlayer.MediaOpened += mediaOpenedHandler;
                
                // Подписываемся на событие CurrentStateChanged для отслеживания состояния
                TypedEventHandler<MediaPlayer, object>? stateChangedHandler = null;
                stateChangedHandler = (sender, args) =>
                {
                    var state = newMediaPlayer.CurrentState;
                    System.Diagnostics.Debug.WriteLine($"[ProjectionDisplay] MediaPlayer state changed: {state}");
                    
                    // Если MediaPlayer в состоянии Opening, Buffering или Paused, пытаемся запустить воспроизведение
                    if (state == MediaPlayerState.Opening || state == MediaPlayerState.Buffering || state == MediaPlayerState.Paused)
                    {
                        System.Diagnostics.Debug.WriteLine($"[ProjectionDisplay] MediaPlayer is {state}, calling Play()");
                        newMediaPlayer.Play();
                    }
                };
                newMediaPlayer.CurrentStateChanged += stateChangedHandler;
                
                // Устанавливаем новый MediaPlayer в MediaPlayerElement ПЕРЕД установкой Source (для основного и превью)
                if (videoPlayer != null)
                {
                    videoPlayer.SetMediaPlayer(newMediaPlayer);
                }
                if (previewVideoPlayer != null)
                {
                    // Для превью создаём отдельный MediaPlayer, чтобы избежать конфликтов
                    try
                    {
                        var previewMediaPlayer = new MediaPlayer();
                        previewVideoPlayer.SetMediaPlayer(previewMediaPlayer);
                    }
                    catch (Exception exPv)
                    {
                        System.Diagnostics.Debug.WriteLine($"[ProjectionDisplay] Failed to set MediaPlayer for preview: {exPv.Message}");
                    }
                }
                System.Diagnostics.Debug.WriteLine("[ProjectionDisplay] Set new MediaPlayer in MediaPlayerElement");
                
                // Задержка для инициализации MediaPlayer в MediaPlayerElement
                await Task.Delay(200);
                
                // Создаем новый MediaSource из нового MediaStreamSource
                var newMediaSource = MediaSource.CreateFromMediaStreamSource(newMediaStreamSource);
                System.Diagnostics.Debug.WriteLine("[ProjectionDisplay] Created new MediaSource from MediaStreamSource");
                
                // Устанавливаем Source в MediaPlayerElement (используя уже установленный MediaPlayer)
                if (videoPlayer != null)
                {
                    videoPlayer.Source = newMediaSource;
                }
                if (previewVideoPlayer != null)
                {
                    try
                    {
                        previewVideoPlayer.Source = newMediaSource;
                    }
                    catch (Exception exPv2)
                    {
                        System.Diagnostics.Debug.WriteLine($"[ProjectionDisplay] Failed to set preview Source: {exPv2.Message}");
                    }
                }
                System.Diagnostics.Debug.WriteLine("[ProjectionDisplay] Set new MediaSource in MediaPlayerElement");
                
                // Задержка перед запуском воспроизведения
                await Task.Delay(200);
                
                // Запускаем воспроизведение
                newMediaPlayer.Play();
                System.Diagnostics.Debug.WriteLine("[ProjectionDisplay] Play() called on new MediaPlayer");
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
                // Отписываемся от события
                if (_cameraMediaStreamSource != null)
                {
                    _cameraMediaStreamSource.MediaStreamSourceChanged -= OnMediaStreamSourceChanged;
                }
                
                var videoPlayer = _stage?.VideoPlayerElement ?? _window?.VideoPlayerElement;
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
        if (_window is not null)
        {
            StopTopMostKeeper();
            _hotkeyDispatcher.DetachProjection();
            ReturnStageToPreview();
            if (_viewModel is not null)
            {
                _viewModel.BackgroundMediaChanged -= OnBackgroundMediaChanged;
            }

            _window.Closed -= OnWindowClosed;
            _syncedBackgroundVideoPath = null;
            _window = null;
            ProjectionWindowVisibilityChanged?.Invoke(this, false);
        }
    }

    private async Task TrySetFullScreenOnSelectedDisplayAsync(ProjectionWindow window)
    {
        try
        {
            var hwnd = WindowNative.GetWindowHandle(window);
            var windowId = Win32Interop.GetWindowIdFromWindow(hwnd);
            var appWindow = AppWindow.GetFromWindowId(windowId);
            
            // Получаем выбранный экран из настроек
            var selectedDisplayId = await _displaySettingsService.GetSelectedDisplayIdAsync();
            System.Diagnostics.Debug.WriteLine($"=== Попытка открыть проектор на выбранном экране ===");
            System.Diagnostics.Debug.WriteLine($"Выбранный DisplayId из настроек: '{selectedDisplayId ?? "null"}'");
            
            var selectedDisplay = await _displaySettingsService.GetSelectedDisplayAsync();
            DisplayArea? targetDisplayArea = null;
            
            if (selectedDisplay is null)
            {
                System.Diagnostics.Debug.WriteLine("ВНИМАНИЕ: selectedDisplay = null (экран не найден в списке доступных)");
                if (selectedDisplayId is not null)
                {
                    System.Diagnostics.Debug.WriteLine($"  Возможно, ID '{selectedDisplayId}' не соответствует ни одному из доступных экранов");
                    var allDisplays = await _displaySettingsService.GetAvailableDisplaysAsync();
                    System.Diagnostics.Debug.WriteLine($"  Доступно экранов: {allDisplays.Count}");
                    foreach (var display in allDisplays)
                    {
                        System.Diagnostics.Debug.WriteLine($"    - ID: '{display.Id}', Name: '{display.Name}', X={display.X}, Y={display.Y}");
                    }
                }
            }
            else
            {
                System.Diagnostics.Debug.WriteLine($"✓ Найден выбранный экран: ID={selectedDisplay.Id}, Name={selectedDisplay.Name}, X={selectedDisplay.X}, Y={selectedDisplay.Y}, W={selectedDisplay.Width}, H={selectedDisplay.Height}, IsPrimary={selectedDisplay.IsPrimary}");
            }
            
            if (selectedDisplay is not null)
            {
                
                // Пытаемся найти DisplayArea по ID или координатам
                try
                {
                    // Если ID это числовой DisplayId
                    if (ulong.TryParse(selectedDisplay.Id, out var displayIdValue))
                    {
                        try
                        {
                            var displayId = new Microsoft.UI.DisplayId { Value = displayIdValue };
                            targetDisplayArea = DisplayArea.GetFromDisplayId(displayId);
                            System.Diagnostics.Debug.WriteLine($"Найден экран по DisplayId: {displayIdValue}");
                        }
                        catch (Exception ex)
                        {
                            System.Diagnostics.Debug.WriteLine($"Не удалось найти по DisplayId {displayIdValue}: {ex.Message}");
                        }
                    }
                    
                    // Если не нашли по ID, ищем по координатам
                    if (targetDisplayArea is null)
                    {
                        // Сначала пытаемся получить DisplayArea напрямую по координатам точки
                        try
                        {
                            var point = new PointInt32(selectedDisplay.X, selectedDisplay.Y);
                            targetDisplayArea = DisplayArea.GetFromPoint(point, DisplayAreaFallback.Nearest);
                            System.Diagnostics.Debug.WriteLine($"✓ Найден DisplayArea через GetFromPoint для точки ({selectedDisplay.X}, {selectedDisplay.Y})");
                        }
                        catch (Exception ex)
                        {
                            System.Diagnostics.Debug.WriteLine($"Не удалось получить DisplayArea через GetFromPoint: {ex.Message}");
                        }
                        
                        // Если не нашли через GetFromPoint, перебираем все DisplayArea
                        if (targetDisplayArea is null)
                        {
                            var allDisplayAreas = DisplayArea.FindAll();
                            System.Diagnostics.Debug.WriteLine($"Всего DisplayArea найдено: {allDisplayAreas.Count}");
                            
                            // Сначала пытаемся найти точное совпадение по координатам
                            System.Diagnostics.Debug.WriteLine($"Ищем по точным координатам: X={selectedDisplay.X}, Y={selectedDisplay.Y}");
                        foreach (var da in allDisplayAreas)
                        {
                            var workArea = da.WorkArea;
                            try
                            {
                                ulong displayId = 0;
                                try
                                {
                                    displayId = da.DisplayId.Value;
                                    System.Diagnostics.Debug.WriteLine($"  DisplayArea: ID={displayId}, X={workArea.X}, Y={workArea.Y}, W={workArea.Width}, H={workArea.Height}");
                                }
                                catch
                                {
                                    System.Diagnostics.Debug.WriteLine($"  DisplayArea: X={workArea.X}, Y={workArea.Y}, W={workArea.Width}, H={workArea.Height} (ID недоступен)");
                                }
                            }
                            catch (Exception ex)
                            {
                                System.Diagnostics.Debug.WriteLine($"  DisplayArea: X={workArea.X}, Y={workArea.Y}, W={workArea.Width}, H={workArea.Height} (ошибка получения ID: {ex.Message})");
                            }
                            
                            // Точное совпадение координат (начало экрана)
                            if (workArea.X == selectedDisplay.X && workArea.Y == selectedDisplay.Y)
                            {
                                targetDisplayArea = da;
                                System.Diagnostics.Debug.WriteLine($"✓ Найден экран по точным координатам WorkArea!");
                                break;
                            }
                        }
                        
                        // Если не нашли точное совпадение, ищем по попаданию точки в область
                        if (targetDisplayArea is null)
                        {
                            System.Diagnostics.Debug.WriteLine("Точное совпадение не найдено, ищем по попаданию точки в область...");
                            foreach (var da in allDisplayAreas)
                            {
                                var workArea = da.WorkArea;
                                
                                // Проверяем, попадает ли точка (X, Y) выбранного экрана в область этого DisplayArea
                                var pointInArea = selectedDisplay.X >= workArea.X && 
                                                 selectedDisplay.X < workArea.X + workArea.Width &&
                                                 selectedDisplay.Y >= workArea.Y && 
                                                 selectedDisplay.Y < workArea.Y + workArea.Height;
                                
                                if (pointInArea)
                                {
                                    targetDisplayArea = da;
                                    System.Diagnostics.Debug.WriteLine($"✓ Найден экран по попаданию точки в область!");
                                    break;
                                }
                            }
                        }
                        
                        // Если все еще не нашли, проверяем попадание в расширенную область (с учетом того, что WorkArea меньше)
                        if (targetDisplayArea is null)
                        {
                            System.Diagnostics.Debug.WriteLine("Поиск по WorkArea не дал результатов, проверяем расширенную область...");
                            foreach (var da in allDisplayAreas)
                            {
                                var workArea = da.WorkArea;
                                
                                // Проверяем, находится ли точка рядом с WorkArea (в пределах 100 пикселей)
                                // Это нужно, так как WorkArea может быть меньше полного размера монитора
                                var nearArea = selectedDisplay.X >= workArea.X - 100 && 
                                             selectedDisplay.X < workArea.X + workArea.Width + 100 &&
                                             selectedDisplay.Y >= workArea.Y - 100 && 
                                             selectedDisplay.Y < workArea.Y + workArea.Height + 100;
                                
                                if (nearArea)
                                {
                                    targetDisplayArea = da;
                                    System.Diagnostics.Debug.WriteLine($"✓ Найден экран по близости к WorkArea!");
                                    break;
                                }
                            }
                        }
                        
                        // Если все еще не нашли, используем поиск по координатам X,Y с погрешностью
                        if (targetDisplayArea is null)
                        {
                            System.Diagnostics.Debug.WriteLine("Поиск по близости не дал результатов, ищем по координатам X,Y с погрешностью...");
                            foreach (var da in allDisplayAreas)
                            {
                                var workArea = da.WorkArea;
                                
                                // Сравниваем только координаты X, Y (размеры могут отличаться из-за панели задач)
                                var xMatch = Math.Abs(workArea.X - selectedDisplay.X) < 50;
                                var yMatch = Math.Abs(workArea.Y - selectedDisplay.Y) < 50;
                                
                                if (xMatch && yMatch)
                                {
                                    targetDisplayArea = da;
                                    System.Diagnostics.Debug.WriteLine($"✓ Найден экран по координатам X,Y с погрешностью!");
                                    break;
                                }
                            }
                        }
                        
                            if (targetDisplayArea is null)
                            {
                                System.Diagnostics.Debug.WriteLine("✗ Не удалось найти DisplayArea по координатам!");
                            }
                        }
                    }
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"Ошибка при поиске DisplayArea: {ex.Message}");
                }
            }
            
            // Если не нашли конкретный экран, используем основной (точка 0,0)
            if (targetDisplayArea is null)
            {
                if (selectedDisplay is null)
                {
                    System.Diagnostics.Debug.WriteLine("Экран не выбран в настройках, используем основной экран");
                }
                else
                {
                    System.Diagnostics.Debug.WriteLine($"Экран '{selectedDisplay.Name}' не найден среди DisplayArea, используем основной экран");
                }
                
                try
                {
                    targetDisplayArea = DisplayArea.GetFromPoint(new PointInt32(0, 0), DisplayAreaFallback.Nearest);
                    System.Diagnostics.Debug.WriteLine($"Используем DisplayArea из точки (0,0)");
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"Ошибка при получении DisplayArea из точки (0,0): {ex.Message}");
                    // Если не удалось получить DisplayArea, используем первый доступный
                    var allDisplayAreas = DisplayArea.FindAll();
                    if (allDisplayAreas.Count > 0)
                    {
                        targetDisplayArea = allDisplayAreas[0];
                        System.Diagnostics.Debug.WriteLine($"Используем первый доступный DisplayArea");
                    }
                }
            }
            
            // Перемещаем окно на выбранный экран перед установкой полноэкранного режима
            if (targetDisplayArea is not null)
            {
                var workArea = targetDisplayArea.WorkArea;
                System.Diagnostics.Debug.WriteLine($"Перемещаем окно на экран: X={workArea.X}, Y={workArea.Y}, W={workArea.Width}, H={workArea.Height}");
                
                // Перемещаем окно в начало выбранного экрана
                // Используем координаты начала экрана для надежного определения
                appWindow.Move(new PointInt32(workArea.X, workArea.Y));
                
                // Даем окну время переместиться перед установкой полноэкранного режима
                await Task.Delay(200);
                
                // Проверяем, что окно находится на правильном экране
                try
                {
                    var checkPoint = new PointInt32(workArea.X + 10, workArea.Y + 10);
                    var currentDisplayArea = DisplayArea.GetFromPoint(checkPoint, DisplayAreaFallback.Nearest);
                    var currentWorkArea = currentDisplayArea.WorkArea;
                    
                    // Сравниваем по координатам WorkArea вместо DisplayId (более надежно)
                    var isOnCorrectScreen = currentWorkArea.X == workArea.X && currentWorkArea.Y == workArea.Y;
                    
                    if (!isOnCorrectScreen)
                    {
                        System.Diagnostics.Debug.WriteLine($"Окно не на правильном экране! Ожидался X={workArea.X}, Y={workArea.Y}, получен X={currentWorkArea.X}, Y={currentWorkArea.Y}");
                        // Пытаемся переместить еще раз
                        appWindow.Move(new PointInt32(workArea.X, workArea.Y));
                        await Task.Delay(200);
                    }
                    else
                    {
                        try
                        {
                            var displayId = targetDisplayArea.DisplayId.Value;
                            System.Diagnostics.Debug.WriteLine($"Окно успешно перемещено на экран {displayId}");
                        }
                        catch
                        {
                            System.Diagnostics.Debug.WriteLine($"Окно успешно перемещено на экран X={workArea.X}, Y={workArea.Y}");
                        }
                    }
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"Ошибка при проверке экрана: {ex.Message}");
            }
            }
            else
            {
                System.Diagnostics.Debug.WriteLine("DisplayArea не найден, используем основной экран");
            }
            
            // Полноэкранно на выбранном мониторе + AlwaysOnTop:
            // обычный FullScreen перекрывается окнами, перетащенными на этот экран.
            ApplyAlwaysOnTopProjectionSurface(appWindow, hwnd, targetDisplayArea);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Ошибка при установке полноэкранного режима: {ex.Message}");
            // В случае ошибки — всё равно always-on-top на текущем/primary экране
            try
            {
                var hwnd = WindowNative.GetWindowHandle(window);
                var windowId = Win32Interop.GetWindowIdFromWindow(hwnd);
                var appWindow = AppWindow.GetFromWindowId(windowId);
                ApplyAlwaysOnTopProjectionSurface(appWindow, hwnd, DisplayArea.Primary);
            }
            catch
            {
                window.ExtendsContentIntoTitleBar = true;
            }
        }
    }

    private void ApplyAlwaysOnTopProjectionSurface(AppWindow appWindow, IntPtr hwnd, DisplayArea? displayArea)
    {
        var bounds = displayArea?.OuterBounds ?? DisplayArea.Primary.OuterBounds;

        if (_window is not null)
        {
            _window.ExtendsContentIntoTitleBar = true;
            // Без системного backdrop — иначе по краям/углам просвечивает светлая рамка WinUI.
            _window.SystemBackdrop = null;
        }

        appWindow.SetPresenter(AppWindowPresenterKind.Overlapped);
        if (appWindow.Presenter is OverlappedPresenter presenter)
        {
            presenter.SetBorderAndTitleBar(false, false);
            presenter.IsAlwaysOnTop = true;
            presenter.IsResizable = false;
            presenter.IsMaximizable = false;
            presenter.IsMinimizable = false;
        }

        // Windows 11: у overlapped-окон по умолчанию скруглённые углы — отключаем.
        DisableProjectionWindowChrome(hwnd);

        appWindow.MoveAndResize(bounds);
        SetProjectionTopMost(hwnd);
        StartTopMostKeeper(hwnd);
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
        if (_projectionHwnd != IntPtr.Zero && _window is not null)
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


