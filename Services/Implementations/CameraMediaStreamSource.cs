using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices.WindowsRuntime;
using Windows.Media.Core;
using Windows.Media.MediaProperties;
using Windows.Storage.Streams;
using ChyguiSlide.Services.Abstractions;
using ChyguiSlide.Services.Models;
using Microsoft.UI.Dispatching;

namespace ChyguiSlide.Services.Implementations;

public sealed class CameraMediaStreamSource : IDisposable
{
    private readonly ICameraStreamService _cameraStreamService;
    private readonly DispatcherQueue _dispatcher;
    private MediaStreamSource? _mediaStreamSource;
    private VideoStreamDescriptor? _videoStreamDescriptor;
    private byte[]? _spsPpsData;
    private long _currentTimestamp;
    private long _firstVideoTimestamp; // Первый timestamp видео кадра (не config)
    private bool _hasFirstVideoTimestamp;
    private readonly ConcurrentQueue<MediaStreamSample> _sampleQueue = new();
    private MediaStreamSourceSampleRequest? _pendingRequest;
    private const long TicksPerMicrosecond = 10; // 100 nanoseconds per microsecond
    private bool _hasReceivedConfigFrame; // Флаг, что config frame был получен
    private readonly Queue<FrameData> _pendingVideoFrames = new(); // Очередь видео кадров до получения config frame
    private bool _isHevc; // Флаг, что поток использует H.265/HEVC (иначе H.264)
    
    // Событие для уведомления об изменении MediaStreamSource (например, при смене кодека)
    public event EventHandler<MediaStreamSource>? MediaStreamSourceChanged;

    public CameraMediaStreamSource(ICameraStreamService cameraStreamService)
    {
        _cameraStreamService = cameraStreamService;
        _cameraStreamService.FrameReceived += OnFrameReceived;
        _dispatcher = App.MainDispatcherQueue ?? DispatcherQueue.GetForCurrentThread() 
            ?? throw new InvalidOperationException("DispatcherQueue недоступен.");
    }

    public MediaStreamSource CreateMediaStreamSource()
    {
        if (_mediaStreamSource != null)
        {
            return _mediaStreamSource;
        }

        // Создаем дескриптор видео потока для H.264/H.265
        // Параметры будут обновлены при получении первого config frame
        // Начинаем с H.264, так как он более универсален
        // Кодек будет определен при получении config frame
        var videoEncodingProperties = VideoEncodingProperties.CreateH264();
        videoEncodingProperties.Width = 1920;
        videoEncodingProperties.Height = 1080;
        videoEncodingProperties.Bitrate = 5000000; // 5 Mbps
        videoEncodingProperties.FrameRate.Numerator = 30;
        videoEncodingProperties.FrameRate.Denominator = 1;
        
        // Профиль будет определен из SPS при получении config frame

        _videoStreamDescriptor = new VideoStreamDescriptor(videoEncodingProperties);
        _mediaStreamSource = new MediaStreamSource(_videoStreamDescriptor);

        _mediaStreamSource.Starting += OnStarting;
        _mediaStreamSource.SampleRequested += OnSampleRequested;
        _mediaStreamSource.Closed += OnClosed;

        return _mediaStreamSource;
    }
    
    private void RecreateMediaStreamSourceForCodec(bool isHevc)
    {
        // Пересоздаем MediaStreamSource с правильным кодеком
        if (_mediaStreamSource != null)
        {
            _mediaStreamSource.Starting -= OnStarting;
            _mediaStreamSource.SampleRequested -= OnSampleRequested;
            _mediaStreamSource.Closed -= OnClosed;
        }
        
        // Очищаем очередь и pending request, так как они относятся к старому MediaStreamSource
        while (_sampleQueue.TryDequeue(out _)) { }
        _pendingRequest = null;
        System.Diagnostics.Debug.WriteLine("[CameraMediaStreamSource] Cleared sample queue and pending request before recreating MediaStreamSource");
        
        VideoEncodingProperties videoEncodingProperties;
        if (isHevc)
        {
            videoEncodingProperties = VideoEncodingProperties.CreateHevc();
            System.Diagnostics.Debug.WriteLine("[CameraMediaStreamSource] Creating HEVC/H.265 VideoEncodingProperties");
        }
        else
        {
            videoEncodingProperties = VideoEncodingProperties.CreateH264();
            System.Diagnostics.Debug.WriteLine("[CameraMediaStreamSource] Creating H.264 VideoEncodingProperties");
        }
        
        videoEncodingProperties.Width = 1920;
        videoEncodingProperties.Height = 1080;
        videoEncodingProperties.Bitrate = 5000000; // 5 Mbps
        videoEncodingProperties.FrameRate.Numerator = 30;
        videoEncodingProperties.FrameRate.Denominator = 1;
        
        // Устанавливаем CodecPrivateData (VPS/SPS/PPS для H.265, SPS/PPS для H.264)
        // Это критично для инициализации декодера
        // В WinRT API для VideoEncodingProperties CodecPrivateData устанавливается через GUID
        // MF_MT_MPEG_SEQUENCE_HEADER = {C892E55B-252D-42B5-A316-D997D515B6FE}
        if (_spsPpsData != null && _spsPpsData.Length > 0)
        {
            try
            {
                var buffer = _spsPpsData.AsBuffer();
                // Используем GUID для MF_MT_MPEG_SEQUENCE_HEADER
                var mfMtMpegSequenceHeaderGuid = new Guid("C892E55B-252D-42B5-A316-D997D515B6FE");
                videoEncodingProperties.Properties[mfMtMpegSequenceHeaderGuid] = buffer;
                System.Diagnostics.Debug.WriteLine($"[CameraMediaStreamSource] Set MF_MT_MPEG_SEQUENCE_HEADER for {(isHevc ? "HEVC/H.265" : "H.264")}, size={_spsPpsData.Length}");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[CameraMediaStreamSource] Failed to set CodecPrivateData: {ex.Message}");
                // Media Foundation должен сам извлечь SPS/PPS из config sample
            }
        }
        else
        {
            System.Diagnostics.Debug.WriteLine($"[CameraMediaStreamSource] No SPS/PPS data available to set CodecPrivateData for {(isHevc ? "HEVC/H.265" : "H.264")}");
        }
        
        _videoStreamDescriptor = new VideoStreamDescriptor(videoEncodingProperties);
        _mediaStreamSource = new MediaStreamSource(_videoStreamDescriptor);
        
        _mediaStreamSource.Starting += OnStarting;
        _mediaStreamSource.SampleRequested += OnSampleRequested;
        _mediaStreamSource.Closed += OnClosed;
        
        _isHevc = isHevc;
        System.Diagnostics.Debug.WriteLine($"[CameraMediaStreamSource] MediaStreamSource recreated for {(isHevc ? "HEVC/H.265" : "H.264")}");
        
        // Уведомляем подписчиков об изменении MediaStreamSource
        MediaStreamSourceChanged?.Invoke(this, _mediaStreamSource);
    }

    private void OnStarting(MediaStreamSource sender, MediaStreamSourceStartingEventArgs args)
    {
        var isCurrent = sender == _mediaStreamSource;
        System.Diagnostics.Debug.WriteLine($"[CameraMediaStreamSource] OnStarting called for {(isCurrent ? "CURRENT" : "OLD")} MediaStreamSource, codec={(_isHevc ? "HEVC/H.265" : "H.264")}, sender hash={sender.GetHashCode()}, _mediaStreamSource hash={_mediaStreamSource?.GetHashCode() ?? 0}");
        
        // Если это не текущий MediaStreamSource, игнорируем событие
        if (!isCurrent)
        {
            System.Diagnostics.Debug.WriteLine($"[CameraMediaStreamSource] OnStarting: Ignoring event from old MediaStreamSource");
            return;
        }
        
        _currentTimestamp = 0;
        _pendingRequest = null;
        _firstVideoTimestamp = 0;
        _hasFirstVideoTimestamp = false;
        // НЕ сбрасываем _hasReceivedConfigFrame и _isHevc при старте, так как они уже установлены
        // _hasReceivedConfigFrame = false;
        // _isHevc = false; // Сбрасываем флаг кодека при старте
        
        // Очищаем очередь (но сохраняем _spsPpsData)
        while (_sampleQueue.TryDequeue(out _)) { }
        while (_pendingVideoFrames.TryDequeue(out _)) { }
        
        // Устанавливаем начальную позицию
        args.Request.SetActualStartPosition(TimeSpan.Zero);
        
        System.Diagnostics.Debug.WriteLine($"[CameraMediaStreamSource] Starting: SPS/PPS data available: {_spsPpsData != null && _spsPpsData.Length > 0}, codec={(_isHevc ? "HEVC/H.265" : "H.264")}");
        
        // Если есть сохраненные SPS/PPS данные, добавляем их в очередь первыми
        if (_spsPpsData != null && _spsPpsData.Length > 0)
        {
            var sample = CreateMediaStreamSample(_spsPpsData, 0, isKeyFrame: false, isConfig: true);
            if (sample != null)
            {
                _sampleQueue.Enqueue(sample);
                System.Diagnostics.Debug.WriteLine($"[CameraMediaStreamSource] SPS/PPS sample added to queue on start (codec={(_isHevc ? "HEVC/H.265" : "H.264")}, queue size={_sampleQueue.Count})");
            }
        }
    }

    private void OnSampleRequested(MediaStreamSource sender, MediaStreamSourceSampleRequestedEventArgs args)
    {
        var isCurrent = sender == _mediaStreamSource;
        System.Diagnostics.Debug.WriteLine($"[CameraMediaStreamSource] Sample requested from {(isCurrent ? "CURRENT" : "OLD")} MediaStreamSource, queue size: {_sampleQueue.Count}, pending: {_pendingRequest != null}, sender hash={sender.GetHashCode()}, _mediaStreamSource hash={_mediaStreamSource?.GetHashCode() ?? 0}");
        
        // Если это не текущий MediaStreamSource, игнорируем запрос
        if (!isCurrent)
        {
            System.Diagnostics.Debug.WriteLine($"[CameraMediaStreamSource] OnSampleRequested: Ignoring request from old MediaStreamSource");
            return;
        }
        
        // Проверяем, есть ли sample в очереди
        if (_sampleQueue.TryDequeue(out var sample))
        {
            // Отправляем sample немедленно
            args.Request.Sample = sample;
            _pendingRequest = null;
            System.Diagnostics.Debug.WriteLine($"[CameraMediaStreamSource] Sample provided: Duration={sample.Duration}, Timestamp={sample.Timestamp}, KeyFrame={sample.KeyFrame}");
        }
        else
        {
            // Сохраняем запрос, чтобы отправить sample когда он придет
            _pendingRequest = args.Request;
            System.Diagnostics.Debug.WriteLine("[CameraMediaStreamSource] No sample available, waiting...");
        }
    }

    private void OnClosed(MediaStreamSource sender, MediaStreamSourceClosedEventArgs args)
    {
        // Cleanup при закрытии
    }

    private void OnFrameReceived(object? sender, FrameData frameData)
    {
        // Обрабатываем в UI потоке, так как MediaStreamSource требует этого
        _dispatcher.TryEnqueue(() =>
        {
            if (_mediaStreamSource == null || _videoStreamDescriptor == null)
            {
                System.Diagnostics.Debug.WriteLine("[CameraMediaStreamSource] OnFrameReceived: MediaStreamSource or VideoStreamDescriptor is null");
                return;
            }

            try
            {
                // Используем информацию из FrameData
                bool isConfig = frameData.IsConfig;
                bool isKeyFrame = frameData.IsKeyFrame;

                System.Diagnostics.Debug.WriteLine($"[CameraMediaStreamSource] OnFrameReceived: Size={frameData.Data.Length}, Timestamp={frameData.Timestamp}, IsKeyFrame={isKeyFrame}, IsConfig={isConfig}");

                if (isConfig)
                {
                    _hasReceivedConfigFrame = true;
                    
                    // Логируем первые байты config frame для диагностики
                    var hexPreview = string.Join(" ", frameData.Data.Take(Math.Min(64, frameData.Data.Length)).Select(b => b.ToString("X2")));
                    System.Diagnostics.Debug.WriteLine($"[CameraMediaStreamSource] Config frame received, size={frameData.Data.Length}, first bytes: {hexPreview}");
                    
                    // Проверяем, есть ли SPS в config frame (H.264 или H.265)
                    var spsOffset = FindSPSOffset(frameData.Data, out bool isHevcFromConfig);
                    if (spsOffset >= 0)
                    {
                        System.Diagnostics.Debug.WriteLine($"[CameraMediaStreamSource] SPS found in config frame at offset {spsOffset}, codec={(isHevcFromConfig ? "HEVC/H.265" : "H.264")}");
                        
                        // Сохраняем config frame данные ДО пересоздания MediaStreamSource,
                        // чтобы CodecPrivateData был установлен правильно
                        _spsPpsData = frameData.Data;
                        
                        // Если кодек отличается от текущего, пересоздаем MediaStreamSource
                        if (isHevcFromConfig != _isHevc)
                        {
                            System.Diagnostics.Debug.WriteLine($"[CameraMediaStreamSource] Codec mismatch detected: current={(_isHevc ? "HEVC/H.265" : "H.264")}, detected={(isHevcFromConfig ? "HEVC/H.265" : "H.264")}, recreating MediaStreamSource");
                            RecreateMediaStreamSourceForCodec(isHevcFromConfig);
                        }
                        
                        // Обновляем свойства потока на основе SPS/PPS
                        UpdateStreamProperties(frameData.Data, isHevcFromConfig);
                        
                        // Config frame содержит SPS, отправляем его
                        var configSample = CreateMediaStreamSample(frameData.Data, 0, isKeyFrame: false, isConfig: true);
                        if (configSample != null)
                        {
                            System.Diagnostics.Debug.WriteLine($"[CameraMediaStreamSource] Config sample with SPS created, providing to pending request or queue");
                            if (_pendingRequest != null)
                            {
                                _pendingRequest.Sample = configSample;
                                _pendingRequest = null;
                                System.Diagnostics.Debug.WriteLine("[CameraMediaStreamSource] Config sample provided to pending request");
                            }
                            else
                            {
                                _sampleQueue.Enqueue(configSample);
                                System.Diagnostics.Debug.WriteLine($"[CameraMediaStreamSource] Config sample added to queue, queue size: {_sampleQueue.Count}");
                            }
                        }
                    }
                    else
                    {
                        // Если не нашли SPS, но определили кодек по VPS/PPS, все равно используем эти данные
                        if (isHevcFromConfig && _spsPpsData == null)
                        {
                            System.Diagnostics.Debug.WriteLine("[CameraMediaStreamSource] No SPS found in config frame, but H.265 stream detected - will use VPS/PPS");
                            // Если кодек отличается от текущего, пересоздаем MediaStreamSource
                            if (isHevcFromConfig != _isHevc)
                            {
                                System.Diagnostics.Debug.WriteLine($"[CameraMediaStreamSource] Codec mismatch detected: current={(_isHevc ? "HEVC/H.265" : "H.264")}, detected=HEVC/H.265, recreating MediaStreamSource");
                                RecreateMediaStreamSourceForCodec(isHevcFromConfig);
                            }
                            _spsPpsData = frameData.Data;
                            UpdateStreamProperties(frameData.Data, isHevcFromConfig);
                            
                            var configSample = CreateMediaStreamSample(frameData.Data, 0, isKeyFrame: false, isConfig: true);
                            if (configSample != null)
                            {
                                if (_pendingRequest != null)
                                {
                                    _pendingRequest.Sample = configSample;
                                    _pendingRequest = null;
                                }
                                else
                                {
                                    _sampleQueue.Enqueue(configSample);
                                }
                            }
                        }
                        else
                        {
                            System.Diagnostics.Debug.WriteLine("[CameraMediaStreamSource] No SPS found in config frame - will try to extract from first key frame");
                            // Config frame не содержит SPS, НЕ устанавливаем _spsPpsData
                            // Это позволит проверить key frames на наличие SPS/PPS
                        }
                    }
                    
                    // Теперь обрабатываем отложенные видео кадры
                    System.Diagnostics.Debug.WriteLine($"[CameraMediaStreamSource] Processing {_pendingVideoFrames.Count} pending video frames after config frame");
                    while (_pendingVideoFrames.TryDequeue(out var pendingFrame))
                    {
                        ProcessVideoFrame(pendingFrame);
                    }
                    
                    // Не отправляем config frame как обычный кадр, он уже обработан выше
                    return;
                }
                
                // Если config frame еще не получен, откладываем видео кадры
                if (!_hasReceivedConfigFrame)
                {
                    System.Diagnostics.Debug.WriteLine($"[CameraMediaStreamSource] Config frame not received yet, queuing video frame (queue size: {_pendingVideoFrames.Count})");
                    _pendingVideoFrames.Enqueue(frameData);
                    return;
                }
                
                // Обрабатываем видео кадр
                ProcessVideoFrame(frameData);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[CameraMediaStreamSource] Error processing frame: {ex.Message}\n{ex.StackTrace}");
            }
        });
    }
    
    private void ProcessVideoFrame(FrameData frameData)
    {
        try
        {
            bool isKeyFrame = frameData.IsKeyFrame;
            
            // Проверяем, есть ли SPS/PPS в key frame (они могут быть в начале key frame)
            // Это критично, если config frame не содержит SPS
            if (isKeyFrame && _spsPpsData == null)
            {
                System.Diagnostics.Debug.WriteLine("[CameraMediaStreamSource] Key frame received but no SPS/PPS data, checking for SPS/PPS in key frame");
                // Извлекаем SPS/PPS из key frame (первые NAL units до первого IDR)
                // ExtractSPSPPSFromKeyFrame ищет как SPS (тип 7), так и PPS (тип 8)
                var extractedSpsPps = ExtractSPSPPSFromKeyFrame(frameData.Data);
                if (extractedSpsPps != null && extractedSpsPps.Length > 0)
                {
                    System.Diagnostics.Debug.WriteLine($"[CameraMediaStreamSource] Successfully extracted SPS/PPS from key frame (size={extractedSpsPps.Length})");
                    _spsPpsData = extractedSpsPps;
                    _hasReceivedConfigFrame = true; // Отмечаем, что config frame (SPS/PPS) получен
                    // Определяем кодек из извлеченных данных
                    FindSPSOffset(extractedSpsPps, out bool isHevcFromExtracted);
                    UpdateStreamProperties(_spsPpsData, isHevcFromExtracted);
                    
                    // Обрабатываем отложенные видео кадры
                    System.Diagnostics.Debug.WriteLine($"[CameraMediaStreamSource] Processing {_pendingVideoFrames.Count} pending video frames after SPS/PPS extraction");
                    while (_pendingVideoFrames.TryDequeue(out var pendingFrame))
                    {
                        ProcessVideoFrame(pendingFrame);
                    }
                    
                    // Отправляем SPS/PPS sample ПЕРЕД первым видео кадром
                    System.Diagnostics.Debug.WriteLine($"[CameraMediaStreamSource] Sending extracted SPS/PPS sample (size={_spsPpsData.Length}) before first video frame");
                    var spsPpsSample = CreateMediaStreamSample(_spsPpsData, 0, isKeyFrame: false, isConfig: true);
                    if (spsPpsSample != null)
                    {
                        if (_pendingRequest != null)
                        {
                            _pendingRequest.Sample = spsPpsSample;
                            _pendingRequest = null;
                            System.Diagnostics.Debug.WriteLine("[CameraMediaStreamSource] Extracted SPS/PPS sample provided to pending request");
                        }
                        else
                        {
                            // Вставляем в начало очереди, чтобы он был отправлен перед видео кадрами
                            // В WinRT нет прямого метода для вставки в начало очереди, 
                            // поэтому создаем временную очередь
                            var tempQueue = new Queue<MediaStreamSample>();
                            tempQueue.Enqueue(spsPpsSample);
                            while (_sampleQueue.TryDequeue(out var existingSample))
                            {
                                tempQueue.Enqueue(existingSample);
                            }
                            while (tempQueue.TryDequeue(out var queuedSample))
                            {
                                _sampleQueue.Enqueue(queuedSample);
                            }
                            System.Diagnostics.Debug.WriteLine($"[CameraMediaStreamSource] Extracted SPS/PPS sample inserted at queue front, queue size: {_sampleQueue.Count}");
                        }
                    }
                    else
                    {
                        System.Diagnostics.Debug.WriteLine("[CameraMediaStreamSource] Failed to create SPS/PPS sample from extracted data");
                    }
                }
                else
                {
                    System.Diagnostics.Debug.WriteLine("[CameraMediaStreamSource] Failed to extract SPS/PPS from key frame - decoder may not initialize");
                }
            }

            // Нормализуем timestamp: для первого видео кадра сохраняем его timestamp как базовый
            long normalizedTimestamp = frameData.Timestamp;
            if (!_hasFirstVideoTimestamp)
            {
                _firstVideoTimestamp = frameData.Timestamp;
                _hasFirstVideoTimestamp = true;
                normalizedTimestamp = 0; // Первый видео кадр начинается с 0
                System.Diagnostics.Debug.WriteLine($"[CameraMediaStreamSource] First video frame timestamp: {frameData.Timestamp}μs, normalized to 0");
            }
            else
            {
                // Вычитаем базовый timestamp для нормализации
                normalizedTimestamp = frameData.Timestamp - _firstVideoTimestamp;
            }
            
            // Создаем sample для MediaStreamSource с нормализованным timestamp
            var sample = CreateMediaStreamSample(frameData.Data, normalizedTimestamp, isKeyFrame, isConfig: false);
            if (sample != null)
            {
                System.Diagnostics.Debug.WriteLine($"[CameraMediaStreamSource] Video sample created, providing to pending request or queue");
                // Если есть ожидающий запрос, отправляем sample сразу
                if (_pendingRequest != null)
                {
                    _pendingRequest.Sample = sample;
                    _pendingRequest = null;
                    System.Diagnostics.Debug.WriteLine("[CameraMediaStreamSource] Video sample provided to pending request");
                }
                else
                {
                    // Иначе добавляем в очередь
                    _sampleQueue.Enqueue(sample);
                    System.Diagnostics.Debug.WriteLine($"[CameraMediaStreamSource] Video sample added to queue, queue size: {_sampleQueue.Count}");
                }
            }
            else
            {
                System.Diagnostics.Debug.WriteLine("[CameraMediaStreamSource] Failed to create video sample");
            }

            // Используем нормализованный timestamp
            _currentTimestamp = normalizedTimestamp;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[CameraMediaStreamSource] Error processing video frame: {ex.Message}\n{ex.StackTrace}");
        }
    }
    
    private byte[]? ExtractSPSPPSFromKeyFrame(byte[] keyFrameData)
    {
        // Извлекаем все SPS/PPS NAL units из key frame
        // H.264: SPS = type 7, PPS = type 8, IDR = type 5
        // H.265: VPS = type 32, SPS = type 33, PPS = type 34, IDR = type 19/20
        System.Diagnostics.Debug.WriteLine($"[CameraMediaStreamSource] ExtractSPSPPSFromKeyFrame: Searching in {keyFrameData.Length} bytes");
        List<byte[]> spsPpsNals = new();
        int offset = 0;
        int nalCount = 0;
        bool foundIdr = false;
        
        while (offset < keyFrameData.Length - 4)
        {
            // Логируем текущий offset для диагностики (только каждые 64KB, чтобы не засорять логи)
            if (offset % (64 * 1024) == 0 || offset < 10000)
            {
                System.Diagnostics.Debug.WriteLine($"[CameraMediaStreamSource] ExtractSPSPPSFromKeyFrame: Searching at offset {offset}/{keyFrameData.Length} ({(offset * 100.0 / keyFrameData.Length):F1}%)");
            }
            
            // Ищем start code
            if (keyFrameData[offset] == 0x00 && keyFrameData[offset + 1] == 0x00)
            {
                int nalStart = -1;
                
                if (keyFrameData[offset + 2] == 0x00 && keyFrameData[offset + 3] == 0x01)
                {
                    nalStart = offset + 4;
                }
                else if (keyFrameData[offset + 2] == 0x01)
                {
                    nalStart = offset + 3;
                }
                
                if (nalStart >= 0 && nalStart < keyFrameData.Length)
                {
                    byte nalTypeH264 = (byte)(keyFrameData[nalStart] & 0x1F); // H.264: нижние 5 бит
                    byte nalTypeH265 = (byte)((keyFrameData[nalStart] >> 1) & 0x3F); // H.265: биты 1-6
                    nalCount++;
                    System.Diagnostics.Debug.WriteLine($"[CameraMediaStreamSource] ExtractSPSPPSFromKeyFrame: Found NAL unit #{nalCount} at offset {offset}, H.264 type={nalTypeH264}, H.265 type={nalTypeH265} (H.264: 7=SPS, 8=PPS, 5=IDR; H.265: 33=SPS, 34=PPS, 19/20=IDR)");
                    
                    // Если это SPS или PPS (H.264 или H.265), извлекаем NAL unit
                    bool isSpsPps = (nalTypeH264 == 7 || nalTypeH264 == 8) || (nalTypeH265 == 32 || nalTypeH265 == 33 || nalTypeH265 == 34);
                    
                    // Проверяем, является ли это IDR (только для соответствующего кодека)
                    // Для H.264: IDR = тип 5
                    // Для H.265: IDR = тип 19 или 20
                    // НО: не проверяем H.265 IDR для H.264 потока, так как это может дать ложные срабатывания
                    // (например, H.264 тип 8 (PPS) может дать H.265 тип 20 при неправильной интерпретации)
                    bool isIdr = false;
                    if (isSpsPps)
                    {
                        // Если это SPS/PPS, это не IDR
                        isIdr = false;
                    }
                    else
                    {
                        // Проверяем IDR только если это не SPS/PPS
                        // Для H.264 потока проверяем только H.264 IDR (тип 5)
                        // Для H.265 потока проверяем только H.265 IDR (тип 19/20)
                        // Пока проверяем только H.264, так как из логов видно, что это H.264 поток
                        isIdr = (nalTypeH264 == 5);
                    }
                    
                    if (isIdr && !foundIdr)
                    {
                        foundIdr = true;
                        System.Diagnostics.Debug.WriteLine($"[CameraMediaStreamSource] ExtractSPSPPSFromKeyFrame: Found IDR (H.264 type={nalTypeH264}), will stop after extracting remaining SPS/PPS");
                    }
                    
                    if (isSpsPps)
                    {
                        // Ищем следующий start code для определения длины
                        // Для SPS/PPS ищем по всему key frame, так как они обычно маленькие
                        // но следующий start code может быть далеко, если текущий NAL unit большой
                        int nextStart = -1;
                        // Ищем следующий start code во всем key frame
                        // Для SPS/PPS используем более консервативный лимит (128KB), так как они обычно маленькие
                        // Но если не найдем, продолжим поиск дальше
                        const int maxSearchDistance = 128 * 1024; // 128KB для SPS/PPS
                        int searchEnd = Math.Min(nalStart + 1 + maxSearchDistance, keyFrameData.Length - 4);
                        for (int i = nalStart + 1; i < searchEnd; i++)
                        {
                            if (keyFrameData[i] == 0x00 && keyFrameData[i + 1] == 0x00)
                            {
                                if (keyFrameData[i + 2] == 0x00 && keyFrameData[i + 3] == 0x01)
                                {
                                    nextStart = i;
                                    break;
                                }
                                else if (keyFrameData[i + 2] == 0x01)
                                {
                                    nextStart = i;
                                    break;
                                }
                            }
                        }
                        
                        if (nextStart < 0 && searchEnd < keyFrameData.Length - 4)
                        {
                            System.Diagnostics.Debug.WriteLine($"[CameraMediaStreamSource] ExtractSPSPPSFromKeyFrame: Next start code not found within {maxSearchDistance} bytes from offset {nalStart}, will continue search after extracting this NAL unit");
                        }
                        
                        int nalLength;
                        if (nextStart >= 0)
                        {
                            nalLength = nextStart - offset;
                        }
                        else
                        {
                            // Если следующий start code не найден, ограничиваем максимальную длину NAL unit
                            // SPS/PPS обычно не превышают 1-2KB, но для безопасности используем 4KB
                            // Это позволит нам продолжить поиск дальше по key frame
                            const int maxNalLength = 4 * 1024; // 4KB для SPS/PPS
                            int remainingData = keyFrameData.Length - offset;
                            nalLength = Math.Min(remainingData, maxNalLength);
                            System.Diagnostics.Debug.WriteLine($"[CameraMediaStreamSource] ExtractSPSPPSFromKeyFrame: Next start code not found within 128KB, limiting NAL unit to {nalLength} bytes (remaining={remainingData}, max={maxNalLength}) and continuing search");
                        }
                        
                        byte[] nalUnit = new byte[nalLength];
                        Array.Copy(keyFrameData, offset, nalUnit, 0, nalLength);
                        spsPpsNals.Add(nalUnit);
                        System.Diagnostics.Debug.WriteLine($"[CameraMediaStreamSource] ExtractSPSPPSFromKeyFrame: Extracted NAL unit H.264 type={nalTypeH264}, H.265 type={nalTypeH265} (offset={offset}, length={nalLength})");
                        
                        // Переходим к следующему start code
                        if (nextStart >= 0)
                        {
                            offset = nextStart;
                            System.Diagnostics.Debug.WriteLine($"[CameraMediaStreamSource] ExtractSPSPPSFromKeyFrame: Moving to next start code at offset {nextStart}, continuing search for more SPS/PPS");
                        }
                        else
                        {
                            // Если следующий start code не найден в пределах 128KB, продолжаем поиск дальше
                            // Это может быть, если PPS очень большой или следующий start code находится дальше
                            // Пропускаем текущий NAL unit и продолжаем поиск по всему key frame
                            int skipOffset = offset + nalLength;
                            if (skipOffset < keyFrameData.Length - 4)
                            {
                                System.Diagnostics.Debug.WriteLine($"[CameraMediaStreamSource] ExtractSPSPPSFromKeyFrame: Next start code not found within 128KB after NAL unit type {nalTypeH264}, skipping {nalLength} bytes and continuing search from offset {skipOffset} (remaining={keyFrameData.Length - skipOffset} bytes)");
                                offset = skipOffset;
                                // Продолжаем поиск - вернемся в начало цикла while, который будет искать следующий start code
                                continue;
                            }
                            else
                            {
                                // Достигли конца key frame
                                System.Diagnostics.Debug.WriteLine($"[CameraMediaStreamSource] ExtractSPSPPSFromKeyFrame: Reached end of key frame after NAL unit type {nalTypeH264}. Extracted {spsPpsNals.Count} SPS/PPS units so far.");
                                break;
                            }
                        }
                        
                        // Если мы уже нашли IDR и извлекли хотя бы один SPS/PPS, можем остановиться
                        if (foundIdr && spsPpsNals.Count > 0)
                        {
                            System.Diagnostics.Debug.WriteLine($"[CameraMediaStreamSource] ExtractSPSPPSFromKeyFrame: Found IDR and extracted {spsPpsNals.Count} SPS/PPS, stopping");
                            break;
                        }
                    }
                    else if (isIdr && foundIdr)
                    {
                        // Если мы уже нашли IDR ранее и извлекли SPS/PPS, останавливаемся
                        if (spsPpsNals.Count > 0)
                        {
                            System.Diagnostics.Debug.WriteLine($"[CameraMediaStreamSource] ExtractSPSPPSFromKeyFrame: Found second IDR, stopping extraction");
                            break;
                        }
                        // Если SPS/PPS еще не найдены, продолжаем поиск после IDR
                        int nextStart = -1;
                        for (int i = nalStart + 1; i < keyFrameData.Length - 4; i++)
                        {
                            if (keyFrameData[i] == 0x00 && keyFrameData[i + 1] == 0x00)
                            {
                                if (keyFrameData[i + 2] == 0x00 && keyFrameData[i + 3] == 0x01)
                                {
                                    nextStart = i;
                                    break;
                                }
                                else if (keyFrameData[i + 2] == 0x01)
                                {
                                    nextStart = i;
                                    break;
                                }
                            }
                        }
                        offset = nextStart >= 0 ? nextStart : keyFrameData.Length;
                    }
                    else
                    {
                        // Это не SPS/PPS/IDR, пропускаем этот NAL unit
                        // Ищем следующий start code
                        int nextStart = -1;
                        for (int i = nalStart + 1; i < keyFrameData.Length - 4; i++)
                        {
                            if (keyFrameData[i] == 0x00 && keyFrameData[i + 1] == 0x00)
                            {
                                if (keyFrameData[i + 2] == 0x00 && keyFrameData[i + 3] == 0x01)
                                {
                                    nextStart = i;
                                    break;
                                }
                                else if (keyFrameData[i + 2] == 0x01)
                                {
                                    nextStart = i;
                                    break;
                                }
                            }
                        }
                        offset = nextStart >= 0 ? nextStart : keyFrameData.Length;
                    }
                }
                else
                {
                    offset++;
                }
            }
            else
            {
                offset++;
            }
        }
        
        // Проверяем, нашли ли мы хотя бы один SPS (H.264 или H.265)
        bool foundSps = false;
        bool detectedHevc = false;
        foreach (var nal in spsPpsNals)
        {
            if (nal.Length >= 5)
            {
                // Определяем offset после start code
                int nalDataOffset = 0;
                if (nal.Length >= 4 && nal[0] == 0x00 && nal[1] == 0x00)
                {
                    if (nal[2] == 0x00 && nal[3] == 0x01)
                    {
                        nalDataOffset = 4;
                    }
                    else if (nal[2] == 0x01)
                    {
                        nalDataOffset = 3;
                    }
                }
                
                if (nal.Length > nalDataOffset)
                {
                    byte nalTypeH264 = (byte)(nal[nalDataOffset] & 0x1F);
                    byte nalTypeH265 = (byte)((nal[nalDataOffset] >> 1) & 0x3F);
                    
                    if (nalTypeH264 == 7) // H.264 SPS
                    {
                        foundSps = true;
                        detectedHevc = false;
                        break;
                    }
                    if (nalTypeH265 == 33) // H.265 SPS
                    {
                        foundSps = true;
                        detectedHevc = true;
                        break;
                    }
                    // Также проверяем H.265 VPS как индикатор H.265 потока
                    if (nalTypeH265 == 32) // H.265 VPS
                    {
                        detectedHevc = true;
                    }
                }
            }
        }
        
        if (spsPpsNals.Count > 0)
        {
            if (!foundSps)
            {
                System.Diagnostics.Debug.WriteLine($"[CameraMediaStreamSource] ExtractSPSPPSFromKeyFrame: WARNING: Found {spsPpsNals.Count} SPS/PPS NAL units, but NO SPS found! Only PPS/VPS found. This may cause decoder initialization issues.");
            }
            
            // Если определили кодек, обновляем его
            if (detectedHevc != _isHevc && detectedHevc)
            {
                System.Diagnostics.Debug.WriteLine($"[CameraMediaStreamSource] ExtractSPSPPSFromKeyFrame: Detected HEVC/H.265 stream, recreating MediaStreamSource");
                RecreateMediaStreamSourceForCodec(detectedHevc);
            }
            
            // Объединяем все SPS/PPS NAL units
            int totalLength = spsPpsNals.Sum(nal => nal.Length);
            byte[] result = new byte[totalLength];
            int resultOffset = 0;
            foreach (var nal in spsPpsNals)
            {
                Array.Copy(nal, 0, result, resultOffset, nal.Length);
                resultOffset += nal.Length;
            }
            System.Diagnostics.Debug.WriteLine($"[CameraMediaStreamSource] ExtractSPSPPSFromKeyFrame: Extracted {spsPpsNals.Count} SPS/PPS NAL units, total length={totalLength}, foundSps={foundSps}, codec={(detectedHevc ? "HEVC/H.265" : "H.264")}");
            return result;
        }
        
        System.Diagnostics.Debug.WriteLine($"[CameraMediaStreamSource] ExtractSPSPPSFromKeyFrame: No SPS/PPS found (checked {nalCount} NAL units)");
        return null;
    }

    private MediaStreamSample? CreateMediaStreamSample(byte[] data, long timestamp, bool isKeyFrame, bool isConfig)
    {
        try
        {
            var buffer = data.AsBuffer();
            
            // Конвертируем timestamp из микросекунд в тики (1 тик = 100 наносекунд = 0.1 микросекунды)
            // timestamp в микросекундах, нужно умножить на 10 чтобы получить тики
            var timeSpan = TimeSpan.FromTicks(timestamp * TicksPerMicrosecond);
            
            // Для config frames используем нулевую длительность, так как это не видео кадр
            // Для обычных кадров используем примерную длительность (33ms для 30fps)
            TimeSpan duration = isConfig ? TimeSpan.Zero : TimeSpan.FromMilliseconds(33.33);
            
            System.Diagnostics.Debug.WriteLine($"[CameraMediaStreamSource] Creating sample: data size={data.Length}, timestamp={timestamp}μs={timeSpan.TotalMilliseconds}ms, duration={duration.TotalMilliseconds}ms, isKeyFrame={isKeyFrame}, isConfig={isConfig}");
            
            var sample = MediaStreamSample.CreateFromBuffer(buffer, timeSpan);
            
            // Устанавливаем длительность для sample
            sample.Duration = duration;
            
            sample.KeyFrame = isKeyFrame;
            sample.Discontinuous = isConfig; // Config frames считаются разрывными
            
            return sample;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[CameraMediaStreamSource] Error creating sample: {ex.Message}\n{ex.StackTrace}");
            return null;
        }
    }

    private bool IsConfigFrame(byte[] data)
    {
        // Проверяем NAL unit type для H.264/H.265
        // H.264: SPS = 7, PPS = 8
        // H.265: VPS = 32, SPS = 33, PPS = 34
        if (data.Length < 5)
        {
            return false;
        }

        // Ищем start code (0x00 0x00 0x00 0x01 или 0x00 0x00 0x01)
        int offset = 0;
        if (data[0] == 0x00 && data[1] == 0x00)
        {
            if (data[2] == 0x00 && data[3] == 0x01)
            {
                offset = 4;
            }
            else if (data[2] == 0x01)
            {
                offset = 3;
            }
        }

        if (offset == 0 || offset >= data.Length)
        {
            return false;
        }

        byte nalType = (byte)(data[offset] & 0x1F); // H.264: нижние 5 бит
        byte nalTypeHevc = (byte)((data[offset] >> 1) & 0x3F); // H.265: биты 1-6

        // H.264 config frames
        if (nalType == 7 || nalType == 8)
        {
            return true;
        }

        // H.265 config frames
        if (nalTypeHevc == 32 || nalTypeHevc == 33 || nalTypeHevc == 34)
        {
            return true;
        }

        return false;
    }

    private bool IsKeyFrame(byte[] data)
    {
        // Проверяем, является ли это keyframe (I-frame)
        if (data.Length < 5)
        {
            return false;
        }

        int offset = 0;
        if (data[0] == 0x00 && data[1] == 0x00)
        {
            if (data[2] == 0x00 && data[3] == 0x01)
            {
                offset = 4;
            }
            else if (data[2] == 0x01)
            {
                offset = 3;
            }
        }

        if (offset == 0 || offset >= data.Length)
        {
            return false;
        }

        byte nalType = (byte)(data[offset] & 0x1F); // H.264
        byte nalTypeHevc = (byte)((data[offset] >> 1) & 0x3F); // H.265

        // H.264: IDR frame = 5
        if (nalType == 5)
        {
            return true;
        }

        // H.265: IDR frame = 19 или 20
        if (nalTypeHevc == 19 || nalTypeHevc == 20)
        {
            return true;
        }

        return false;
    }

    private void UpdateStreamProperties(byte[] spsPpsData, bool isHevc)
    {
        try
        {
            // Парсим SPS для получения реальных размеров
            var spsInfo = ParseSPS(spsPpsData, isHevc);
            if (spsInfo.HasValue)
            {
                System.Diagnostics.Debug.WriteLine($"[CameraMediaStreamSource] SPS parsed: Width={spsInfo.Value.Width}, Height={spsInfo.Value.Height}, Profile={spsInfo.Value.ProfileIdc}, Level={spsInfo.Value.LevelIdc}, Codec={(isHevc ? "HEVC/H.265" : "H.264")}");
                
                // Примечание: VideoStreamDescriptor нельзя изменить после создания MediaStreamSource
                // Media Foundation должен определить параметры из SPS/PPS данных в config frame
                // Если размеры не совпадают, возможно потребуется пересоздать MediaStreamSource
                if (_videoStreamDescriptor != null)
                {
                    var currentProps = _videoStreamDescriptor.EncodingProperties;
                    if (currentProps.Width != (uint)spsInfo.Value.Width || 
                        currentProps.Height != (uint)spsInfo.Value.Height)
                    {
                        System.Diagnostics.Debug.WriteLine($"[CameraMediaStreamSource] Warning: SPS dimensions ({spsInfo.Value.Width}x{spsInfo.Value.Height}) differ from descriptor ({currentProps.Width}x{currentProps.Height})");
                    }
                }
            }
            else
            {
                System.Diagnostics.Debug.WriteLine($"[CameraMediaStreamSource] Failed to parse SPS data (codec={(isHevc ? "HEVC/H.265" : "H.264")})");
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[CameraMediaStreamSource] Error parsing SPS: {ex.Message}\n{ex.StackTrace}");
        }
    }

    private struct SPSInfo
    {
        public int Width;
        public int Height;
        public byte ProfileIdc;
        public byte LevelIdc;
    }

    private SPSInfo? ParseSPS(byte[] data, bool isHevc)
    {
        if (data == null || data.Length < 5)
        {
            System.Diagnostics.Debug.WriteLine($"[CameraMediaStreamSource] ParseSPS: Invalid data (null or too short, length={data?.Length ?? 0})");
            return null;
        }

        try
        {
            // Логируем первые байты для диагностики
            var hexPreview = string.Join(" ", data.Take(Math.Min(32, data.Length)).Select(b => b.ToString("X2")));
            System.Diagnostics.Debug.WriteLine($"[CameraMediaStreamSource] ParseSPS: Data length={data.Length}, codec={(isHevc ? "HEVC/H.265" : "H.264")}, first bytes: {hexPreview}");
            
            // Для H.265 пока не парсим SPS (требуется более сложный парсер)
            // Просто возвращаем null, Media Foundation должен сам определить параметры из SPS/PPS
            if (isHevc)
            {
                System.Diagnostics.Debug.WriteLine("[CameraMediaStreamSource] ParseSPS: H.265 SPS parsing not implemented, Media Foundation will parse it");
                return null;
            }
            
            // Ищем SPS NAL unit (type 7 для H.264, type 33 для H.265) в данных
            int offset = FindSPSOffset(data, out bool detectedHevc);
            if (offset < 0)
            {
                System.Diagnostics.Debug.WriteLine($"[CameraMediaStreamSource] ParseSPS: SPS NAL unit not found in data (codec={(isHevc ? "HEVC/H.265" : "H.264")})");
                return null;
            }
            
            // Если определили другой кодек, обновляем
            if (detectedHevc != isHevc)
            {
                System.Diagnostics.Debug.WriteLine($"[CameraMediaStreamSource] ParseSPS: Codec mismatch: expected={(isHevc ? "HEVC/H.265" : "H.264")}, detected={(detectedHevc ? "HEVC/H.265" : "H.264")}");
                if (detectedHevc)
                {
                    // Это H.265, не парсим
                    return null;
                }
            }
            
            System.Diagnostics.Debug.WriteLine($"[CameraMediaStreamSource] ParseSPS: Found H.264 SPS at offset {offset}");

            // Удаляем emulation prevention bytes (0x00 0x00 0x03 -> 0x00 0x00)
            var rbsp = RemoveEmulationPrevention(data, offset);
            if (rbsp == null || rbsp.Length < 4)
            {
                return null;
            }

            // Создаем простой битовый читатель
            var reader = new BitReader(rbsp);
            
            // Пропускаем NAL header (уже пропущен при поиске offset)
            // profile_idc: u(8)
            byte profileIdc = (byte)reader.ReadBits(8);
            
            // constraint_set flags + reserved_zero_2bits + level_idc: u(8)
            reader.ReadBits(8); // constraint flags и reserved
            byte levelIdc = (byte)reader.ReadBits(8);
            
            // seq_parameter_set_id: ue(v)
            int seqParamSetId = reader.ReadExpGolomb();
            
            // Пропускаем chroma_format_idc и связанные поля для профилей >= 100
            if (profileIdc == 100 || profileIdc == 110 || profileIdc == 122 || 
                profileIdc == 244 || profileIdc == 44 || profileIdc == 83 || 
                profileIdc == 86 || profileIdc == 118 || profileIdc == 128 || 
                profileIdc == 138 || profileIdc == 139 || profileIdc == 134)
            {
                int chromaFormatIdc = reader.ReadExpGolomb();
                if (chromaFormatIdc == 3)
                {
                    reader.ReadBits(1); // separate_colour_plane_flag
                }
                reader.ReadExpGolomb(); // bit_depth_luma_minus8
                reader.ReadExpGolomb(); // bit_depth_chroma_minus8
                reader.ReadBits(1); // qpprime_y_zero_transform_bypass_flag
                
                if (reader.ReadBits(1) != 0) // seq_scaling_matrix_present_flag
                {
                    // Пропускаем scaling lists
                    for (int i = 0; i < 8; i++)
                    {
                        if (reader.ReadBits(1) != 0) // seq_scaling_list_present_flag[i]
                        {
                            int size = (i < 6) ? 16 : 64;
                            SkipScalingList(reader, size);
                        }
                    }
                }
            }
            
            // log2_max_frame_num_minus4: ue(v)
            reader.ReadExpGolomb();
            
            // pic_order_cnt_type: ue(v)
            int picOrderCntType = reader.ReadExpGolomb();
            if (picOrderCntType == 0)
            {
                reader.ReadExpGolomb(); // log2_max_pic_order_cnt_lsb_minus4
            }
            else if (picOrderCntType == 1)
            {
                reader.ReadBits(1); // delta_pic_order_always_zero_flag
                reader.ReadExpGolomb(); // offset_for_non_ref_pic
                reader.ReadExpGolomb(); // offset_for_top_to_bottom_field
                int numRefFramesInPicOrderCntCycle = reader.ReadExpGolomb();
                for (int i = 0; i < numRefFramesInPicOrderCntCycle; i++)
                {
                    reader.ReadExpGolomb(); // offset_for_ref_frame[i]
                }
            }
            
            // max_num_ref_frames: ue(v)
            reader.ReadExpGolomb();
            
            // gaps_in_frame_num_value_allowed_flag: u(1)
            reader.ReadBits(1);
            
            // pic_width_in_mbs_minus1: ue(v)
            int picWidthInMbsMinus1 = reader.ReadExpGolomb();
            
            // pic_height_in_map_units_minus1: ue(v)
            int picHeightInMapUnitsMinus1 = reader.ReadExpGolomb();
            
            // frame_mbs_only_flag: u(1)
            int frameMbsOnlyFlag = reader.ReadBits(1);
            
            if (frameMbsOnlyFlag == 0)
            {
                reader.ReadBits(1); // mb_adaptive_frame_field_flag
            }
            
            reader.ReadBits(1); // direct_8x8_inference_flag
            
            // frame_cropping_flag: u(1)
            int frameCroppingFlag = reader.ReadBits(1);
            
            int cropLeft = 0, cropRight = 0, cropTop = 0, cropBottom = 0;
            if (frameCroppingFlag != 0)
            {
                cropLeft = reader.ReadExpGolomb();
                cropRight = reader.ReadExpGolomb();
                cropTop = reader.ReadExpGolomb();
                cropBottom = reader.ReadExpGolomb();
            }
            
            // Вычисляем размеры
            int picWidthInMbs = picWidthInMbsMinus1 + 1;
            int picHeightInMapUnits = picHeightInMapUnitsMinus1 + 1;
            int frameHeightInMbs = (2 - frameMbsOnlyFlag) * picHeightInMapUnits;
            
            // Базовые размеры в пикселях (макроблоки 16x16)
            int width = picWidthInMbs * 16;
            int height = frameHeightInMbs * 16;
            
            // Применяем crop offsets (в единицах chroma для 4:2:0)
            // Для упрощения предполагаем chroma_format_idc = 1 (4:2:0)
            if (frameCroppingFlag != 0)
            {
                width -= (cropLeft + cropRight) * 2; // SubWidthC = 2 для 4:2:0
                height -= (cropTop + cropBottom) * 2 * (2 - frameMbsOnlyFlag); // SubHeightC = 2 для 4:2:0
            }
            
            return new SPSInfo
            {
                Width = width,
                Height = height,
                ProfileIdc = profileIdc,
                LevelIdc = levelIdc
            };
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[CameraMediaStreamSource] Error in ParseSPS: {ex.Message}");
            return null;
        }
    }

    private int FindSPSOffset(byte[] data, out bool isHevc)
    {
        // Ищем start code и проверяем NAL unit type
        // Также проверяем AVCC формат (length prefix вместо start codes)
        System.Diagnostics.Debug.WriteLine($"[CameraMediaStreamSource] FindSPSOffset: Searching in {data.Length} bytes");
        
        isHevc = false;
        
        // Сначала пробуем найти все NAL units для диагностики
        List<(int offset, byte nalTypeH264, byte nalTypeH265, string format)> foundNals = new();
        
        for (int i = 0; i < data.Length - 4; i++)
        {
            // Проверяем Annex-B формат (start codes)
            if (data[i] == 0x00 && data[i + 1] == 0x00)
            {
                int offset = -1;
                string format = "";
                if (data[i + 2] == 0x00 && data[i + 3] == 0x01)
                {
                    offset = i + 4;
                    format = "Annex-B (4-byte)";
                }
                else if (data[i + 2] == 0x01)
                {
                    offset = i + 3;
                    format = "Annex-B (3-byte)";
                }
                
                if (offset >= 0 && offset < data.Length)
                {
                    // Проверяем NAL unit type (H.264: нижние 5 бит, H.265: биты 1-6)
                    byte nalTypeH264 = (byte)(data[offset] & 0x1F);
                    byte nalTypeH265 = (byte)((data[offset] >> 1) & 0x3F);
                    foundNals.Add((offset, nalTypeH264, nalTypeH265, format));
                    System.Diagnostics.Debug.WriteLine($"[CameraMediaStreamSource] FindSPSOffset: Found NAL unit at offset {offset}, H.264 type={nalTypeH264}, H.265 type={nalTypeH265} (H.264: 7=SPS, 8=PPS; H.265: 32=VPS, 33=SPS, 34=PPS), format={format}");
                    
                    // Проверяем H.264 SPS (тип 7)
                    if (nalTypeH264 == 7) // SPS (H.264)
                    {
                        System.Diagnostics.Debug.WriteLine($"[CameraMediaStreamSource] FindSPSOffset: Found H.264 SPS at offset {offset} (type={nalTypeH264})");
                        isHevc = false;
                        return offset;
                    }
                    
                    // Проверяем H.265 SPS (тип 33)
                    if (nalTypeH265 == 33) // SPS (H.265)
                    {
                        System.Diagnostics.Debug.WriteLine($"[CameraMediaStreamSource] FindSPSOffset: Found H.265 SPS at offset {offset} (type={nalTypeH265})");
                        isHevc = true;
                        return offset;
                    }
                    
                    // Также проверяем H.265 VPS (тип 32) как индикатор H.265 потока
                    // Если находим VPS, но еще не нашли SPS, продолжаем поиск
                    if (nalTypeH265 == 32) // VPS (H.265)
                    {
                        System.Diagnostics.Debug.WriteLine($"[CameraMediaStreamSource] FindSPSOffset: Found H.265 VPS at offset {offset} (type={nalTypeH265}), will continue searching for SPS");
                        // Продолжаем поиск SPS
                    }
                }
            }
            
            // ПРИМЕЧАНИЕ: AVCC формат (length prefix) отключен, так как поток использует Annex-B формат (start codes)
            // Проверка AVCC может давать ложные срабатывания внутри Annex-B потока
            // Если в будущем понадобится поддержка AVCC, нужно добавить более строгую проверку
            // (например, проверять, что перед этим нет start codes в пределах последних N байт)
        }
        
        System.Diagnostics.Debug.WriteLine($"[CameraMediaStreamSource] FindSPSOffset: No SPS NAL unit found. Found {foundNals.Count} NAL units total:");
        foreach (var (offset, nalTypeH264, nalTypeH265, format) in foundNals)
        {
            System.Diagnostics.Debug.WriteLine($"  - Offset {offset}: H.264 type={nalTypeH264}, H.265 type={nalTypeH265}, format={format}");
        }
        
        // Если нашли H.265 VPS или PPS, но не нашли SPS, все равно помечаем как H.265
        // Это поможет правильно определить кодек даже если SPS не найден
        foreach (var (offset, nalTypeH264, nalTypeH265, format) in foundNals)
        {
            if (nalTypeH265 == 32 || nalTypeH265 == 33 || nalTypeH265 == 34)
            {
                System.Diagnostics.Debug.WriteLine($"[CameraMediaStreamSource] FindSPSOffset: Detected H.265 stream based on VPS/PPS/SPS NAL units");
                isHevc = true;
                break;
            }
        }
        
        return -1;
    }

    private byte[]? RemoveEmulationPrevention(byte[] data, int startOffset)
    {
        var result = new List<byte>();
        for (int i = startOffset; i < data.Length; i++)
        {
            if (i + 2 < data.Length && 
                data[i] == 0x00 && data[i + 1] == 0x00 && data[i + 2] == 0x03)
            {
                result.Add(0x00);
                result.Add(0x00);
                i += 2; // Пропускаем 0x03
            }
            else
            {
                result.Add(data[i]);
            }
        }
        return result.ToArray();
    }

    private void SkipScalingList(BitReader reader, int size)
    {
        int lastScale = 8;
        int nextScale = 8;
        for (int i = 0; i < size; i++)
        {
            if (nextScale != 0)
            {
                int deltaScale = reader.ReadSignedExpGolomb();
                nextScale = (lastScale + deltaScale + 256) % 256;
            }
            lastScale = (nextScale != 0) ? nextScale : lastScale;
        }
    }

    private class BitReader
    {
        private readonly byte[] _data;
        private int _byteOffset;
        private int _bitOffset;

        public BitReader(byte[] data)
        {
            _data = data;
            _byteOffset = 0;
            _bitOffset = 0;
        }

        public int ReadBits(int count)
        {
            int result = 0;
            for (int i = 0; i < count; i++)
            {
                if (_byteOffset >= _data.Length)
                {
                    throw new InvalidOperationException("End of data");
                }
                
                int bit = (_data[_byteOffset] >> (7 - _bitOffset)) & 1;
                result = (result << 1) | bit;
                
                _bitOffset++;
                if (_bitOffset >= 8)
                {
                    _bitOffset = 0;
                    _byteOffset++;
                }
            }
            return result;
        }

        public int ReadExpGolomb()
        {
            int leadingZeroBits = -1;
            int b = 0;
            while (b == 0)
            {
                b = ReadBits(1);
                leadingZeroBits++;
            }
            
            int codeNum = (1 << leadingZeroBits) - 1 + ReadBits(leadingZeroBits);
            return codeNum;
        }

        public int ReadSignedExpGolomb()
        {
            int codeNum = ReadExpGolomb();
            int sign = (codeNum % 2 == 0) ? -1 : 1;
            return sign * ((codeNum + 1) / 2);
        }
    }

    public void Dispose()
    {
        if (_cameraStreamService != null)
        {
            _cameraStreamService.FrameReceived -= OnFrameReceived;
        }
        
        // Отписываемся от событий MediaStreamSource
        if (_mediaStreamSource != null)
        {
            _mediaStreamSource.Starting -= OnStarting;
            _mediaStreamSource.SampleRequested -= OnSampleRequested;
            _mediaStreamSource.Closed -= OnClosed;
        }
        
        // MediaStreamSource не требует явного Dispose, но очищаем ссылки
        _mediaStreamSource = null;
        _videoStreamDescriptor = null;
        
        // Очищаем очередь
        while (_sampleQueue.TryDequeue(out _)) { }
    }
}






