using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using ChyguiSlide.Services.Abstractions;
using Microsoft.UI.Dispatching;
using static ChyguiSlide.Services.Implementations.NdiNative;

namespace ChyguiSlide.Services.Implementations;

public sealed class NdiReceiverService : INdiReceiverService, IDisposable
{
    private readonly DispatcherQueue _dispatcher;
    private IntPtr _finder;
    private IntPtr _receiver;
    private CancellationTokenSource? _receiveCancellationTokenSource;
    private Task? _receiveTask;
    private bool _isReceiving;
    private string? _connectedSourceName;
    private bool _isInitialized;
    private readonly object _lockObject = new object();

    public NdiReceiverService()
    {
        _dispatcher = App.MainDispatcherQueue ?? DispatcherQueue.GetForCurrentThread() 
            ?? throw new InvalidOperationException("DispatcherQueue недоступен.");
        
        // Проверяем, загружена ли DLL
        if (!NdiNative.IsDllLoaded)
        {
            var programFilesPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "NDI", "NDI 6 Runtime", "v6");
            var programFilesX86Path = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), "NDI", "NDI 6 Runtime", "v6");
            var programFilesPathAlt = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "NDI", "NDI 6 Runtime");
            var programFilesX86PathAlt = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), "NDI", "NDI 6 Runtime");
            
            var errorMessage = "NDI библиотека не найдена или не может быть загружена.\n\n" +
                "Возможные причины:\n" +
                "1. NDI Tools не установлен или установлен некорректно\n" +
                "2. Отсутствуют зависимости DLL (Visual C++ Redistributable)\n" +
                "3. DLL находится в нестандартном месте\n\n" +
                "Проверьте следующие пути:\n" +
                $"  - {programFilesPath}\n" +
                $"  - {programFilesX86Path}\n" +
                $"  - {programFilesPathAlt}\n" +
                $"  - {programFilesX86PathAlt}\n\n" +
                "Убедитесь, что файл Processing.NDI.Lib.x64.dll существует в одной из этих директорий.\n" +
                "Также проверьте, установлены ли Visual C++ Redistributable (x64).\n\n" +
                "Скачать NDI Tools: https://ndi.tv/tools/\n" +
                "Скачать Visual C++ Redistributable: https://aka.ms/vs/17/release/vc_redist.x64.exe";
            
            System.Diagnostics.Debug.WriteLine($"[NdiReceiver] {errorMessage}");
            
            // Проверяем, существует ли DLL в стандартных местах
            var dllPath1 = Path.Combine(programFilesPath, "Processing.NDI.Lib.x64.dll");
            var dllPath2 = Path.Combine(programFilesX86Path, "Processing.NDI.Lib.x64.dll");
            var dllPath3 = Path.Combine(programFilesPathAlt, "Processing.NDI.Lib.x64.dll");
            var dllPath4 = Path.Combine(programFilesX86PathAlt, "Processing.NDI.Lib.x64.dll");
            
            if (File.Exists(dllPath1))
            {
                System.Diagnostics.Debug.WriteLine($"[NdiReceiver] DLL found at {dllPath1}, but failed to load. Check dependencies.");
            }
            else if (File.Exists(dllPath2))
            {
                System.Diagnostics.Debug.WriteLine($"[NdiReceiver] DLL found at {dllPath2}, but failed to load. Check dependencies.");
            }
            else if (File.Exists(dllPath3))
            {
                System.Diagnostics.Debug.WriteLine($"[NdiReceiver] DLL found at {dllPath3}, but failed to load. Check dependencies.");
            }
            else if (File.Exists(dllPath4))
            {
                System.Diagnostics.Debug.WriteLine($"[NdiReceiver] DLL found at {dllPath4}, but failed to load. Check dependencies.");
            }
            else
            {
                System.Diagnostics.Debug.WriteLine($"[NdiReceiver] DLL not found in standard locations.");
            }
            
            throw new DllNotFoundException(errorMessage);
        }
        
        // Инициализируем NDI
        try
        {
            if (!NdiNative.Initialize())
            {
                var errorMessage = "Не удалось инициализировать NDI библиотеку. " +
                    "Убедитесь, что установлен NDI Runtime (NDI Tools). " +
                    "Скачать можно с https://ndi.tv/tools/";
                System.Diagnostics.Debug.WriteLine($"[NdiReceiver] {errorMessage}");
                throw new InvalidOperationException(errorMessage);
            }
            _isInitialized = true;
            
            // Создаем finder для поиска источников
            _finder = NdiNative.FindCreateV2(IntPtr.Zero);
            if (_finder == IntPtr.Zero)
            {
                throw new InvalidOperationException("Не удалось создать NDI finder");
            }
            
            System.Diagnostics.Debug.WriteLine("[NdiReceiver] NDI initialized");
        }
        catch (DllNotFoundException ex)
        {
            var errorMessage = "NDI библиотека не найдена. " +
                "Убедитесь, что установлен NDI Runtime (NDI Tools). " +
                "Ожидаемые пути установки:\n" +
                $"  - {Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "NDI", "NDI 6 Runtime")}\n" +
                $"  - {Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), "NDI", "NDI 6 Runtime")}\n" +
                "Скачать NDI Tools можно с https://ndi.tv/tools/";
            System.Diagnostics.Debug.WriteLine($"[NdiReceiver] {errorMessage}");
            System.Diagnostics.Debug.WriteLine($"[NdiReceiver] Original exception: {ex.Message}");
            throw new InvalidOperationException(errorMessage, ex);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[NdiReceiver] Failed to initialize NDI: {ex.Message}");
            if (_isInitialized)
            {
                try
                {
                    NdiNative.Destroy();
                }
                catch
                {
                    // Игнорируем ошибки при уничтожении
                }
                _isInitialized = false;
            }
            throw;
        }
    }

    public bool IsConnected => _receiver != IntPtr.Zero && _connectedSourceName != null;
    public bool IsReceiving => _isReceiving;

    public event EventHandler<bool>? ConnectionStateChanged;
    public event EventHandler<NdiVideoFrame>? FrameReceived;

    public async Task<List<NdiSource>> GetAvailableSourcesAsync()
    {
        return await Task.Run(() =>
        {
            try
            {
                lock (_lockObject)
                {
                    if (_finder == IntPtr.Zero)
                    {
                        throw new InvalidOperationException("NDI finder not initialized");
                    }

                    // Ждем немного, чтобы источники успели появиться
                    System.Threading.Thread.Sleep(2000);

                    // Получаем список источников
                    uint numSources = 0;
                    IntPtr pSources = NdiNative.FindGetCurrentSources(_finder, ref numSources);

                    var result = new List<NdiSource>();

                    if (pSources != IntPtr.Zero && numSources > 0)
                    {
                        int structSize = Marshal.SizeOf<SourceT>();
                        for (uint i = 0; i < numSources; i++)
                        {
                            IntPtr sourcePtr = new IntPtr(pSources.ToInt64() + i * structSize);
                            SourceT source = Marshal.PtrToStructure<SourceT>(sourcePtr);
                            
                            string name = Marshal.PtrToStringAnsi(source.p_ndi_name) ?? "Unknown";
                            string url = Marshal.PtrToStringAnsi(source.p_url_address) ?? "Unknown";
                            
                            result.Add(new NdiSource
                            {
                                Name = name,
                                IpAddress = url
                            });
                        }
                    }

                    System.Diagnostics.Debug.WriteLine($"[NdiReceiver] Found {result.Count} NDI sources: {string.Join(", ", result.Select(s => s.Name))}");
                    return result;
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[NdiReceiver] Error getting sources: {ex.Message}");
                return new List<NdiSource>();
            }
        });
    }

    public async Task ConnectAsync(string sourceName)
    {
        System.Diagnostics.Debug.WriteLine($"[NdiReceiver] ConnectAsync called for source: {sourceName}");
        try
        {
            // Ждем появления источников перед блокировкой
            System.Diagnostics.Debug.WriteLine($"[NdiReceiver] Waiting for sources to appear...");
            System.Diagnostics.Debug.WriteLine($"[NdiReceiver] About to call Task.Delay(3000)...");
            try
            {
                await Task.Delay(3000);
                System.Diagnostics.Debug.WriteLine($"[NdiReceiver] Task.Delay(3000) completed at {DateTime.Now:HH:mm:ss.fff}");
            }
            catch (Exception delayEx)
            {
                System.Diagnostics.Debug.WriteLine($"[NdiReceiver] Exception in Task.Delay: {delayEx.Message}");
                System.Diagnostics.Debug.WriteLine($"[NdiReceiver] StackTrace: {delayEx.StackTrace}");
                throw;
            }
            System.Diagnostics.Debug.WriteLine($"[NdiReceiver] Wait completed, getting sources...");
            
            System.Diagnostics.Debug.WriteLine($"[NdiReceiver] Starting Task.Run...");
            try
            {
                await Task.Run(() =>
                {
                    try
                    {
                        System.Diagnostics.Debug.WriteLine($"[NdiReceiver] Task.Run started, entering lock...");
                        lock (_lockObject)
                    {
                        try
                        {
                            System.Diagnostics.Debug.WriteLine($"[NdiReceiver] Lock acquired, connecting to source: {sourceName}");

                            // Отключаемся от предыдущего источника, если есть
                            if (_receiver != IntPtr.Zero)
                            {
                                DisconnectInternal();
                            }

                            if (_finder == IntPtr.Zero)
                            {
                                throw new InvalidOperationException("NDI finder not initialized");
                            }

                            // Получаем список источников и ищем нужный
                            uint numSources = 0;
                            IntPtr pSources;
                            try
                            {
                                System.Diagnostics.Debug.WriteLine($"[NdiReceiver] Calling FindGetCurrentSources...");
                                pSources = NdiNative.FindGetCurrentSources(_finder, ref numSources);
                                System.Diagnostics.Debug.WriteLine($"[NdiReceiver] FindGetCurrentSources returned {numSources} sources, pSources={pSources}");
                            }
                            catch (Exception findEx)
                            {
                                System.Diagnostics.Debug.WriteLine($"[NdiReceiver] Error in FindGetCurrentSources: {findEx.Message}");
                                System.Diagnostics.Debug.WriteLine($"[NdiReceiver] StackTrace: {findEx.StackTrace}");
                                throw;
                            }

                            SourceT? foundSource = null;
                            if (pSources != IntPtr.Zero && numSources > 0)
                            {
                                int structSize = Marshal.SizeOf<SourceT>();
                                for (uint i = 0; i < numSources; i++)
                                {
                                    IntPtr sourcePtr = new IntPtr(pSources.ToInt64() + i * structSize);
                                    SourceT source = Marshal.PtrToStructure<SourceT>(sourcePtr);
                                    string name = Marshal.PtrToStringAnsi(source.p_ndi_name) ?? "";
                                    
                                    System.Diagnostics.Debug.WriteLine($"[NdiReceiver] Checking source {i}: '{name}'");
                                    
                                    if (name == sourceName)
                                    {
                                        foundSource = source;
                                        System.Diagnostics.Debug.WriteLine($"[NdiReceiver] Found matching source: {sourceName}");
                                        break;
                                    }
                                }
                            }

                            if (!foundSource.HasValue)
                            {
                                var errorMsg = $"NDI source '{sourceName}' not found. Available sources: {numSources}";
                                System.Diagnostics.Debug.WriteLine($"[NdiReceiver] {errorMsg}");
                                throw new InvalidOperationException(errorMsg);
                            }

                            // Создаем receiver
                            var recvDesc = new RecvCreateV3T
                            {
                                source_to_connect_to = foundSource.Value,
                                color_format = RecvColorFormatE.recv_color_format_BGRX_BGRA,
                                bandwidth = RecvBandwidthE.recv_bandwidth_highest,
                                allow_video_fields = true,
                                p_ndi_recv_name = IntPtr.Zero
                            };

                            System.Diagnostics.Debug.WriteLine($"[NdiReceiver] Creating receiver...");
                            try
                            {
                                _receiver = NdiNative.RecvCreateV3(ref recvDesc);
                                System.Diagnostics.Debug.WriteLine($"[NdiReceiver] RecvCreateV3 returned: {_receiver}");
                            }
                            catch (Exception createEx)
                            {
                                System.Diagnostics.Debug.WriteLine($"[NdiReceiver] Exception in RecvCreateV3: {createEx.Message}");
                                System.Diagnostics.Debug.WriteLine($"[NdiReceiver] StackTrace: {createEx.StackTrace}");
                                throw;
                            }
                            
                            if (_receiver == IntPtr.Zero)
                            {
                                throw new InvalidOperationException("Failed to create NDI receiver (RecvCreateV3 returned null)");
                            }

                            _connectedSourceName = sourceName;
                            System.Diagnostics.Debug.WriteLine($"[NdiReceiver] Connected to source: {sourceName}");
                            System.Diagnostics.Debug.WriteLine($"[NdiReceiver] ConnectAsync completed successfully (inside lock)");
                        }
                        catch (Exception ex)
                        {
                            System.Diagnostics.Debug.WriteLine($"[NdiReceiver] Connection failed: {ex.Message}");
                            System.Diagnostics.Debug.WriteLine($"[NdiReceiver] StackTrace: {ex.StackTrace}");
                            if (ex.InnerException != null)
                            {
                                System.Diagnostics.Debug.WriteLine($"[NdiReceiver] InnerException: {ex.InnerException.Message}");
                            }
                            if (_receiver != IntPtr.Zero)
                            {
                                try
                                {
                                    NdiNative.RecvDestroy(_receiver);
                                }
                                catch (Exception destroyEx)
                                {
                                    System.Diagnostics.Debug.WriteLine($"[NdiReceiver] Error destroying receiver: {destroyEx.Message}");
                                }
                                _receiver = IntPtr.Zero;
                            }
                            _connectedSourceName = null;
                            throw;
                        }
                    }
                    }
                    catch (Exception taskEx)
                    {
                        System.Diagnostics.Debug.WriteLine($"[NdiReceiver] Exception in Task.Run (outside lock): {taskEx.Message}");
                        System.Diagnostics.Debug.WriteLine($"[NdiReceiver] StackTrace: {taskEx.StackTrace}");
                        if (taskEx.InnerException != null)
                        {
                            System.Diagnostics.Debug.WriteLine($"[NdiReceiver] InnerException: {taskEx.InnerException.Message}");
                        }
                        throw;
                    }
                });
                System.Diagnostics.Debug.WriteLine($"[NdiReceiver] Task.Run completed successfully at {DateTime.Now:HH:mm:ss.fff}");
                
                // Уведомляем об изменении состояния подключения ПОСЛЕ завершения Task.Run
                // Это гарантирует, что мы не в lock и не в фоновом потоке
                try
                {
                    System.Diagnostics.Debug.WriteLine($"[NdiReceiver] Notifying connection state change (after Task.Run) at {DateTime.Now:HH:mm:ss.fff}");
                    NotifyConnectionStateChanged(true);
                    System.Diagnostics.Debug.WriteLine($"[NdiReceiver] Connection state notification sent successfully");
                }
                catch (Exception notifyEx)
                {
                    System.Diagnostics.Debug.WriteLine($"[NdiReceiver] Error in NotifyConnectionStateChanged (after Task.Run): {notifyEx.Message}");
                    System.Diagnostics.Debug.WriteLine($"[NdiReceiver] StackTrace: {notifyEx.StackTrace}");
                    System.Diagnostics.Debug.WriteLine($"[NdiReceiver] Exception type: {notifyEx.GetType().FullName}");
                    // Не пробрасываем исключение, чтобы не сломать подключение
                }
            }
            catch (Exception taskRunEx)
            {
                System.Diagnostics.Debug.WriteLine($"[NdiReceiver] Exception waiting for Task.Run: {taskRunEx.Message}");
                System.Diagnostics.Debug.WriteLine($"[NdiReceiver] StackTrace: {taskRunEx.StackTrace}");
                if (taskRunEx.InnerException != null)
                {
                    System.Diagnostics.Debug.WriteLine($"[NdiReceiver] InnerException: {taskRunEx.InnerException.Message}");
                }
                
                // Уведомляем об ошибке подключения
                try
                {
                    NotifyConnectionStateChanged(false);
                }
                catch (Exception notifyEx)
                {
                    System.Diagnostics.Debug.WriteLine($"[NdiReceiver] Error notifying connection failure: {notifyEx.Message}");
                }
                
                throw;
            }
        }
        catch (Exception outerEx)
        {
            System.Diagnostics.Debug.WriteLine($"[NdiReceiver] Outer exception in ConnectAsync: {outerEx.Message}");
            System.Diagnostics.Debug.WriteLine($"[NdiReceiver] StackTrace: {outerEx.StackTrace}");
            if (outerEx.InnerException != null)
            {
                System.Diagnostics.Debug.WriteLine($"[NdiReceiver] InnerException: {outerEx.InnerException.Message}");
            }
            
            // Уведомляем об ошибке подключения
            try
            {
                NotifyConnectionStateChanged(false);
            }
            catch (Exception notifyEx)
            {
                System.Diagnostics.Debug.WriteLine($"[NdiReceiver] Error notifying connection failure (outer): {notifyEx.Message}");
            }
            
            throw;
        }
        finally
        {
            System.Diagnostics.Debug.WriteLine($"[NdiReceiver] ===== ConnectAsync END for source: {sourceName} ===== at {DateTime.Now:HH:mm:ss.fff}");
        }
    }

    public async Task DisconnectAsync()
    {
        await Task.Run(() =>
        {
            DisconnectInternal();
        });
    }

    private void DisconnectInternal()
    {
        try
        {
            System.Diagnostics.Debug.WriteLine("[NdiReceiver] Disconnecting...");
            
            StopReceivingAsync().Wait();

            lock (_lockObject)
            {
                if (_receiver != IntPtr.Zero)
                {
                    NdiNative.RecvDestroy(_receiver);
                    _receiver = IntPtr.Zero;
                }
            }

            _connectedSourceName = null;
            NotifyConnectionStateChanged(false);
            System.Diagnostics.Debug.WriteLine("[NdiReceiver] Disconnected");
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[NdiReceiver] Error during disconnect: {ex.Message}");
        }
    }

    public async Task StartReceivingAsync()
    {
        if (_receiver == IntPtr.Zero)
        {
            var errorMsg = "Not connected to NDI source (receiver is null)";
            System.Diagnostics.Debug.WriteLine($"[NdiReceiver] {errorMsg}");
            throw new InvalidOperationException(errorMsg);
        }

        if (_isReceiving)
        {
            System.Diagnostics.Debug.WriteLine("[NdiReceiver] Already receiving, skipping StartReceivingAsync");
            return;
        }

        try
        {
            _isReceiving = true;
            _receiveCancellationTokenSource = new CancellationTokenSource();
            _receiveTask = Task.Run(() => ReceiveLoop(_receiveCancellationTokenSource.Token));
            
            System.Diagnostics.Debug.WriteLine("[NdiReceiver] Started receiving");
            await Task.CompletedTask;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[NdiReceiver] Error starting receive: {ex.Message}");
            System.Diagnostics.Debug.WriteLine($"[NdiReceiver] StackTrace: {ex.StackTrace}");
            _isReceiving = false;
            throw;
        }
    }

    public async Task StopReceivingAsync()
    {
        if (!_isReceiving)
        {
            return;
        }

        _isReceiving = false;
        _receiveCancellationTokenSource?.Cancel();

        if (_receiveTask != null)
        {
            try
            {
                await Task.WhenAny(_receiveTask, Task.Delay(2000));
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[NdiReceiver] Error stopping receive loop: {ex.Message}");
            }
            finally
            {
                _receiveTask = null;
            }
        }

        _receiveCancellationTokenSource?.Dispose();
        _receiveCancellationTokenSource = null;
        
        System.Diagnostics.Debug.WriteLine("[NdiReceiver] Stopped receiving");
    }

    private void ReceiveLoop(CancellationToken cancellationToken)
    {
        System.Diagnostics.Debug.WriteLine("[NdiReceiver] ReceiveLoop started");
        
        try
        {
            while (_isReceiving && !cancellationToken.IsCancellationRequested)
            {
                cancellationToken.ThrowIfCancellationRequested();

                lock (_lockObject)
                {
                    if (_receiver == IntPtr.Zero)
                    {
                        break;
                    }

                    try
                    {
                        // Получаем видеокадр из NDI (таймаут 1 секунда)
                        var videoFrame = new VideoFrameV2T();
                        var audioFrame = new AudioFrameV2T();
                        var metadataFrame = new MetadataFrameT();

                        FrameTypeE frameType;
                        try
                        {
                            frameType = NdiNative.RecvCaptureV3(_receiver, ref videoFrame, ref audioFrame, ref metadataFrame, 1000);
                        }
                        catch (Exception captureEx)
                        {
                            System.Diagnostics.Debug.WriteLine($"[NdiReceiver] Error in RecvCaptureV3: {captureEx.Message}");
                            System.Diagnostics.Debug.WriteLine($"[NdiReceiver] StackTrace: {captureEx.StackTrace}");
                            System.Threading.Thread.Sleep(100);
                            continue;
                        }

                        if (frameType == FrameTypeE.frame_type_video)
                        {
                            try
                            {
                                // Проверяем валидность данных кадра
                                if (videoFrame.p_data == IntPtr.Zero || videoFrame.xres <= 0 || videoFrame.yres <= 0)
                                {
                                    System.Diagnostics.Debug.WriteLine($"[NdiReceiver] Invalid video frame data: p_data={videoFrame.p_data}, xres={videoFrame.xres}, yres={videoFrame.yres}");
                                    NdiNative.RecvFreeVideoV2(_receiver, ref videoFrame);
                                    continue;
                                }

                                // Копируем данные кадра
                                int dataSize = (int)(videoFrame.line_stride_in_bytes * videoFrame.yres);
                                if (dataSize <= 0 || dataSize > 100 * 1024 * 1024) // Максимум 100MB
                                {
                                    System.Diagnostics.Debug.WriteLine($"[NdiReceiver] Invalid frame data size: {dataSize}");
                                    NdiNative.RecvFreeVideoV2(_receiver, ref videoFrame);
                                    continue;
                                }

                                var frameData = new byte[dataSize];
                                Marshal.Copy(videoFrame.p_data, frameData, 0, dataSize);

                                var frame = new NdiVideoFrame
                                {
                                    Width = videoFrame.xres,
                                    Height = videoFrame.yres,
                                    Stride = videoFrame.line_stride_in_bytes,
                                    Timestamp = videoFrame.timestamp,
                                    Data = frameData
                                };

                                // Отправляем кадр через DispatcherQueue
                                _dispatcher.TryEnqueue(() =>
                                {
                                    try
                                    {
                                        FrameReceived?.Invoke(this, frame);
                                    }
                                    catch (Exception invokeEx)
                                    {
                                        System.Diagnostics.Debug.WriteLine($"[NdiReceiver] Error invoking FrameReceived: {invokeEx.Message}");
                                    }
                                });
                            }
                            catch (Exception frameEx)
                            {
                                System.Diagnostics.Debug.WriteLine($"[NdiReceiver] Error processing video frame: {frameEx.Message}");
                                System.Diagnostics.Debug.WriteLine($"[NdiReceiver] StackTrace: {frameEx.StackTrace}");
                            }
                            finally
                            {
                                // Освобождаем ресурсы кадра
                                try
                                {
                                    NdiNative.RecvFreeVideoV2(_receiver, ref videoFrame);
                                }
                                catch (Exception freeEx)
                                {
                                    System.Diagnostics.Debug.WriteLine($"[NdiReceiver] Error freeing video frame: {freeEx.Message}");
                                }
                            }
                        }
                        else if (frameType == FrameTypeE.frame_type_audio)
                        {
                            // Освобождаем аудио кадр, если он был получен
                            try
                            {
                                NdiNative.RecvFreeAudioV2(_receiver, ref audioFrame);
                            }
                            catch (Exception freeEx)
                            {
                                System.Diagnostics.Debug.WriteLine($"[NdiReceiver] Error freeing audio frame: {freeEx.Message}");
                            }
                        }
                        else if (frameType == FrameTypeE.frame_type_metadata)
                        {
                            // Освобождаем метаданные, если они были получены
                            try
                            {
                                NdiNative.RecvFreeMetadata(_receiver, ref metadataFrame);
                            }
                            catch (Exception freeEx)
                            {
                                System.Diagnostics.Debug.WriteLine($"[NdiReceiver] Error freeing metadata: {freeEx.Message}");
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        System.Diagnostics.Debug.WriteLine($"[NdiReceiver] Error receiving frame: {ex.Message}");
                        System.Diagnostics.Debug.WriteLine($"[NdiReceiver] StackTrace: {ex.StackTrace}");
                        // Не прерываем цикл, продолжаем попытки
                        System.Threading.Thread.Sleep(100);
                    }
                }
            }
        }
        catch (OperationCanceledException)
        {
            System.Diagnostics.Debug.WriteLine("[NdiReceiver] ReceiveLoop cancelled");
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[NdiReceiver] ReceiveLoop error: {ex.Message}");
        }
        finally
        {
            System.Diagnostics.Debug.WriteLine("[NdiReceiver] ReceiveLoop completed");
        }
    }

    private void NotifyConnectionStateChanged(bool connected)
    {
        try
        {
            if (_dispatcher == null)
            {
                System.Diagnostics.Debug.WriteLine($"[NdiReceiver] Dispatcher is null, cannot notify connection state change");
                return;
            }
            
            // Всегда вызываем через DispatcherQueue, даже если мы в UI потоке,
            // чтобы избежать проблем с COMException при вызове из фонового потока
            try
            {
                var enqueued = _dispatcher.TryEnqueue(() =>
                {
                    try
                    {
                        System.Diagnostics.Debug.WriteLine($"[NdiReceiver] Invoking ConnectionStateChanged event, connected={connected}");
                        ConnectionStateChanged?.Invoke(this, connected);
                        System.Diagnostics.Debug.WriteLine($"[NdiReceiver] ConnectionStateChanged event invoked successfully");
                    }
                    catch (Exception ex)
                    {
                        System.Diagnostics.Debug.WriteLine($"[NdiReceiver] Error in ConnectionStateChanged handler: {ex.Message}");
                        System.Diagnostics.Debug.WriteLine($"[NdiReceiver] StackTrace: {ex.StackTrace}");
                        System.Diagnostics.Debug.WriteLine($"[NdiReceiver] Exception type: {ex.GetType().FullName}");
                        System.Diagnostics.Debug.WriteLine($"[NdiReceiver] HResult: 0x{ex.HResult:X8}");
                        if (ex.InnerException != null)
                        {
                            System.Diagnostics.Debug.WriteLine($"[NdiReceiver] InnerException type: {ex.InnerException.GetType().FullName}");
                            System.Diagnostics.Debug.WriteLine($"[NdiReceiver] InnerException message: {ex.InnerException.Message}");
                            System.Diagnostics.Debug.WriteLine($"[NdiReceiver] InnerException HResult: 0x{ex.InnerException.HResult:X8}");
                        }
                        // Не пробрасываем исключение, чтобы не сломать приложение
                    }
                });
                
                if (!enqueued)
                {
                    System.Diagnostics.Debug.WriteLine($"[NdiReceiver] Failed to enqueue connection state change notification");
                }
            }
            catch (Exception dispatchEx)
            {
                System.Diagnostics.Debug.WriteLine($"[NdiReceiver] Error calling TryEnqueue: {dispatchEx.Message}");
                System.Diagnostics.Debug.WriteLine($"[NdiReceiver] StackTrace: {dispatchEx.StackTrace}");
                System.Diagnostics.Debug.WriteLine($"[NdiReceiver] Exception type: {dispatchEx.GetType().FullName}");
                System.Diagnostics.Debug.WriteLine($"[NdiReceiver] HResult: 0x{dispatchEx.HResult:X8}");
                if (dispatchEx.InnerException != null)
                {
                    System.Diagnostics.Debug.WriteLine($"[NdiReceiver] InnerException: {dispatchEx.InnerException.Message}");
                }
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[NdiReceiver] Error in NotifyConnectionStateChanged: {ex.Message}");
            System.Diagnostics.Debug.WriteLine($"[NdiReceiver] StackTrace: {ex.StackTrace}");
            System.Diagnostics.Debug.WriteLine($"[NdiReceiver] Exception type: {ex.GetType().FullName}");
            if (ex.InnerException != null)
            {
                System.Diagnostics.Debug.WriteLine($"[NdiReceiver] InnerException: {ex.InnerException.Message}");
            }
        }
    }

    public void Dispose()
    {
        DisconnectInternal();
        
        lock (_lockObject)
        {
            if (_finder != IntPtr.Zero)
            {
                NdiNative.FindDestroy(_finder);
                _finder = IntPtr.Zero;
            }
        }
        
        if (_isInitialized)
        {
            try
            {
                NdiNative.Destroy();
                _isInitialized = false;
                System.Diagnostics.Debug.WriteLine("[NdiReceiver] NDI destroyed");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[NdiReceiver] Error destroying NDI: {ex.Message}");
            }
        }
    }
}


