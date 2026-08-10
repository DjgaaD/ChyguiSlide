using System;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.WindowsRuntime;
using System.Threading;
using System.Threading.Tasks;
using ChyguiSlide.Services.Abstractions;
using Microsoft.UI.Dispatching;
using Windows.Graphics.Imaging;
using Windows.Storage.Streams;
using Microsoft.UI.Xaml.Media.Imaging;

namespace ChyguiSlide.Services.Implementations;

/// <summary>
/// Компонент для отображения NDI видеокадров в WinUI
/// NDI кадры приходят в формате BGRA32 (незакодированные), поэтому отображаем их напрямую
/// </summary>
public sealed class NdiVideoRenderer : IDisposable
{
    private readonly INdiReceiverService _ndiReceiverService;
    private readonly DispatcherQueue _dispatcher;
    private WriteableBitmap? _bitmap;
    private bool _isRendering;
    private CancellationTokenSource? _renderCancellationTokenSource;
    private Task? _renderTask;

    public event EventHandler<WriteableBitmap>? BitmapUpdated;

    public NdiVideoRenderer(INdiReceiverService ndiReceiverService)
    {
        _ndiReceiverService = ndiReceiverService;
        _ndiReceiverService.FrameReceived += OnFrameReceived;
        _dispatcher = App.MainDispatcherQueue ?? DispatcherQueue.GetForCurrentThread() 
            ?? throw new InvalidOperationException("DispatcherQueue недоступен.");
    }

    public WriteableBitmap? CurrentBitmap => _bitmap;

    public void StartRendering()
    {
        if (_isRendering)
        {
            return;
        }

        _isRendering = true;
        _renderCancellationTokenSource = new CancellationTokenSource();
        _renderTask = Task.Run(() => RenderLoop(_renderCancellationTokenSource.Token));
        
        System.Diagnostics.Debug.WriteLine("[NdiVideoRenderer] Started rendering");
    }

    public void StopRendering()
    {
        if (!_isRendering)
        {
            return;
        }

        _isRendering = false;
        _renderCancellationTokenSource?.Cancel();

        if (_renderTask != null)
        {
            try
            {
                _renderTask.Wait(2000);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[NdiVideoRenderer] Error stopping render loop: {ex.Message}");
            }
            finally
            {
                _renderTask = null;
            }
        }

        _renderCancellationTokenSource?.Dispose();
        _renderCancellationTokenSource = null;
        
        System.Diagnostics.Debug.WriteLine("[NdiVideoRenderer] Stopped rendering");
    }

    private void OnFrameReceived(object? sender, NdiVideoFrame frame)
    {
        if (!_isRendering)
        {
            return;
        }

        // Обновляем bitmap на UI потоке
        _dispatcher.TryEnqueue(() =>
        {
            UpdateBitmap(frame);
        });
    }

    private void UpdateBitmap(NdiVideoFrame frame)
    {
        try
        {
            // Создаем или обновляем WriteableBitmap
            if (_bitmap == null || _bitmap.PixelWidth != frame.Width || _bitmap.PixelHeight != frame.Height)
            {
                _bitmap = new WriteableBitmap(frame.Width, frame.Height);
                System.Diagnostics.Debug.WriteLine($"[NdiVideoRenderer] Created new WriteableBitmap: {frame.Width}x{frame.Height}");
            }

            // Копируем данные кадра в bitmap с учетом stride
            // NDI кадры в формате BGRX (4 байта на пиксель), но могут иметь padding в конце строки
            var bitmapStride = frame.Width * 4; // BGRA32 = 4 байта на пиксель
            var frameStride = frame.Stride;
            
            // Получаем доступ к байтам PixelBuffer через IBuffer
            var pixelBuffer = _bitmap.PixelBuffer;
            
            // Используем AsStream() для работы с байтами
            using (var stream = pixelBuffer.AsStream())
            {
                stream.Seek(0, System.IO.SeekOrigin.Begin);
                
                // Если stride совпадает, копируем напрямую
                if (frameStride == bitmapStride)
                {
                    var copyLength = Math.Min(frame.Data.Length, (int)stream.Length);
                    stream.Write(frame.Data, 0, copyLength);
                }
                else
                {
                    // Копируем построчно, пропуская padding
                    var buffer = new byte[bitmapStride];
                    for (int y = 0; y < frame.Height; y++)
                    {
                        var sourceOffset = y * frameStride;
                        var destOffset = y * bitmapStride;
                        
                        if (sourceOffset + bitmapStride <= frame.Data.Length)
                        {
                            Array.Copy(frame.Data, sourceOffset, buffer, 0, bitmapStride);
                            stream.Seek(destOffset, System.IO.SeekOrigin.Begin);
                            stream.Write(buffer, 0, bitmapStride);
                        }
                    }
                }
            }

            _bitmap.Invalidate();
            
            // Уведомляем подписчиков об обновлении
            BitmapUpdated?.Invoke(this, _bitmap);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[NdiVideoRenderer] Error updating bitmap: {ex.Message}");
        }
    }

    private void RenderLoop(CancellationToken cancellationToken)
    {
        System.Diagnostics.Debug.WriteLine("[NdiVideoRenderer] RenderLoop started");
        
        try
        {
            while (_isRendering && !cancellationToken.IsCancellationRequested)
            {
                // Основная работа выполняется в OnFrameReceived
                // Этот цикл просто поддерживает поток активным
                Thread.Sleep(100);
            }
        }
        catch (OperationCanceledException)
        {
            System.Diagnostics.Debug.WriteLine("[NdiVideoRenderer] RenderLoop cancelled");
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[NdiVideoRenderer] RenderLoop error: {ex.Message}");
        }
        finally
        {
            System.Diagnostics.Debug.WriteLine("[NdiVideoRenderer] RenderLoop completed");
        }
    }

    public void Dispose()
    {
        StopRendering();
        _ndiReceiverService.FrameReceived -= OnFrameReceived;
        _bitmap = null;
    }
}


