using System;
using System.IO;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using ChyguiSlide.Services.Abstractions;
using ChyguiSlide.Services.Models;
using Microsoft.UI.Dispatching;

namespace ChyguiSlide.Services.Implementations;

public sealed class CameraStreamService : ICameraStreamService
{
    private readonly DispatcherQueue _dispatcher;
    private readonly SemaphoreSlim _connectionSemaphore = new SemaphoreSlim(1, 1);
    private TcpClient? _tcpClient;
    private NetworkStream? _stream;
    private CancellationTokenSource? _cancellationTokenSource;
    private Task? _receiveLoopTask;
    private bool _isStreaming;
    private string _host = string.Empty;
    private int _port;

    public CameraStreamService()
    {
        _dispatcher = App.MainDispatcherQueue ?? DispatcherQueue.GetForCurrentThread() 
            ?? throw new InvalidOperationException("DispatcherQueue недоступен.");
    }

    public bool IsConnected => _tcpClient?.Connected ?? false;
    public bool IsStreaming => _isStreaming;

    public event EventHandler<bool>? ConnectionStateChanged;
    public event EventHandler<FrameData>? FrameReceived;

    public async Task ConnectAsync(string host, int port)
    {
        await _connectionSemaphore.WaitAsync();
        try
        {
            System.Diagnostics.Debug.WriteLine($"[CameraStream] ConnectAsync called: host={host}, port={port}");
            
            // Если уже подключены, сначала полностью отключаемся (без семафора, так как он уже захвачен)
            if (IsConnected)
            {
                System.Diagnostics.Debug.WriteLine("[CameraStream] Already connected, disconnecting first...");
                await DisconnectInternalAsync();
                // Даем время системе полностью закрыть соединение
                await Task.Delay(100);
            }

            _host = host;
            _port = port;

            try
            {
                _tcpClient = new TcpClient
                {
                    NoDelay = true, // Disable Nagle's algorithm for lower latency
                    ReceiveBufferSize = 256 * 1024 // 256KB buffer
                };

                // Настраиваем LingerOption для немедленного закрытия соединения
                _tcpClient.LingerState = new System.Net.Sockets.LingerOption(true, 0);

                await _tcpClient.ConnectAsync(host, port);
                _stream = _tcpClient.GetStream();

                // Read stream header
                var header = await ReadStreamHeaderAsync(CancellationToken.None);
                if (header.Magic[0] != 'G' || header.Magic[1] != 'R' || 
                    header.Magic[2] != 'S' || header.Magic[3] != 'T')
                {
                    throw new InvalidOperationException("Invalid stream header magic bytes");
                }

                if (header.Version != 1)
                {
                    throw new InvalidOperationException($"Unsupported protocol version: {header.Version}");
                }

            NotifyConnectionStateChanged(true);
            System.Diagnostics.Debug.WriteLine($"[CameraStream] ConnectAsync completed successfully: host={host}, port={port}");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[CameraStream] Connection failed: {ex.Message}");
                await DisconnectInternalAsync();
                throw;
            }
        }
        finally
        {
            _connectionSemaphore.Release();
        }
    }

    public async Task DisconnectAsync()
    {
        await _connectionSemaphore.WaitAsync();
        try
        {
            await DisconnectInternalAsync();
        }
        finally
        {
            _connectionSemaphore.Release();
        }
    }

    private async Task DisconnectInternalAsync()
    {
        System.Diagnostics.Debug.WriteLine("[CameraStream] DisconnectInternalAsync called");
        
        // Останавливаем стриминг и ждем завершения ReceiveLoop
        await StopStreamingAsync();

        try
        {
            // Закрываем NetworkStream перед TcpClient для правильного закрытия соединения
            if (_stream != null)
            {
                System.Diagnostics.Debug.WriteLine("[CameraStream] Closing NetworkStream");
                try
                {
                    _stream.Close();
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"[CameraStream] Error closing NetworkStream: {ex.Message}");
                }
                finally
                {
                    _stream.Dispose();
                    _stream = null;
                }
            }
            
            if (_tcpClient != null)
            {
                System.Diagnostics.Debug.WriteLine("[CameraStream] Closing TcpClient");
                try
                {
                    // Закрываем сокет явно перед закрытием TcpClient
                    if (_tcpClient.Connected)
                    {
                        _tcpClient.Client?.Shutdown(System.Net.Sockets.SocketShutdown.Both);
                        // Даем время сокету завершить shutdown
                        await Task.Delay(50);
                    }
                    _tcpClient.Close();
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"[CameraStream] Error closing TcpClient: {ex.Message}");
                }
                finally
                {
                    _tcpClient.Dispose();
                    _tcpClient = null;
                }
            }
            
            // Дополнительная задержка для гарантии полного закрытия соединения на стороне сервера
            await Task.Delay(100);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[CameraStream] Error during disconnect cleanup: {ex.Message}");
        }
        finally
        {
            _stream = null;
            _tcpClient = null;
            NotifyConnectionStateChanged(false);
            System.Diagnostics.Debug.WriteLine("[CameraStream] DisconnectInternalAsync completed");
        }
    }

    public async Task StartStreamingAsync()
    {
        if (!IsConnected || _stream == null)
        {
            throw new InvalidOperationException("Not connected to camera stream");
        }

        if (_isStreaming)
        {
            return;
        }

        _isStreaming = true;
        _cancellationTokenSource = new CancellationTokenSource();
        _receiveLoopTask = Task.Run(() => ReceiveLoop(_cancellationTokenSource.Token));
        
        System.Diagnostics.Debug.WriteLine("[CameraStream] StartStreamingAsync: ReceiveLoop task started");
    }

    public async Task StopStreamingAsync()
    {
        System.Diagnostics.Debug.WriteLine("[CameraStream] StopStreamingAsync called");
        
        if (!_isStreaming)
        {
            return;
        }

        _isStreaming = false;
        _cancellationTokenSource?.Cancel();
        
        // Ждем завершения ReceiveLoop с таймаутом
        if (_receiveLoopTask != null)
        {
            try
            {
                System.Diagnostics.Debug.WriteLine("[CameraStream] Waiting for ReceiveLoop to complete...");
                var completedTask = await Task.WhenAny(_receiveLoopTask, Task.Delay(2000)); // Таймаут 2 секунды
                
                if (completedTask == _receiveLoopTask)
                {
                    // ReceiveLoop завершился, ждем его окончания
                    try
                    {
                        await _receiveLoopTask;
                        System.Diagnostics.Debug.WriteLine("[CameraStream] ReceiveLoop completed successfully");
                    }
                    catch (Exception ex)
                    {
                        System.Diagnostics.Debug.WriteLine($"[CameraStream] ReceiveLoop completed with exception: {ex.Message}");
                    }
                }
                else
                {
                    System.Diagnostics.Debug.WriteLine("[CameraStream] ReceiveLoop timeout, forcing termination");
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[CameraStream] Error waiting for ReceiveLoop: {ex.Message}");
            }
            finally
            {
                _receiveLoopTask = null;
            }
        }
        
        _cancellationTokenSource?.Dispose();
        _cancellationTokenSource = null;
        
        System.Diagnostics.Debug.WriteLine("[CameraStream] StopStreamingAsync completed");
    }

    private async Task<StreamHeader> ReadStreamHeaderAsync(CancellationToken cancellationToken = default)
    {
        if (_stream == null)
        {
            throw new InvalidOperationException("Stream is not available");
        }

        var buffer = new byte[16];
        await ReadExactAsync(buffer, 0, 16, cancellationToken);

        // Проверяем magic bytes
        if (buffer[0] != 'G' || buffer[1] != 'R' || buffer[2] != 'S' || buffer[3] != 'T')
        {
            throw new InvalidOperationException("Invalid stream header magic bytes");
        }

        return new StreamHeader
        {
            Magic = new[] { buffer[0], buffer[1], buffer[2], buffer[3] },
            Version = ConvertFromBigEndianInt32(buffer, 4),
            Reserved = ConvertFromBigEndianInt64(buffer, 8)
        };
    }

    private async Task<FrameHeader> ReadFrameHeaderAsync(CancellationToken cancellationToken = default)
    {
        if (_stream == null)
        {
            throw new InvalidOperationException("Stream is not available");
        }

        var buffer = new byte[16];
        await ReadExactAsync(buffer, 0, 16, cancellationToken);

        return new FrameHeader
        {
            Size = ConvertFromBigEndianInt32(buffer, 0),
            Timestamp = ConvertFromBigEndianInt64(buffer, 4),
            Flags = ConvertFromBigEndianInt32(buffer, 12)
        };
    }

    private async Task ReadExactAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken = default)
    {
        if (_stream == null)
        {
            throw new InvalidOperationException("Stream is not available");
        }

        int totalRead = 0;
        while (totalRead < count)
        {
            cancellationToken.ThrowIfCancellationRequested();
            
            var read = await _stream.ReadAsync(buffer, offset + totalRead, count - totalRead, cancellationToken);
            if (read == 0)
            {
                throw new EndOfStreamException("Stream ended unexpectedly");
            }
            totalRead += read;
        }
    }

    private void ReceiveLoop(CancellationToken cancellationToken)
    {
        System.Diagnostics.Debug.WriteLine("[CameraStream] ReceiveLoop started");
        try
        {
            while (_isStreaming && !cancellationToken.IsCancellationRequested && IsConnected)
            {
                cancellationToken.ThrowIfCancellationRequested();
                
                try
                {
                    var frameHeader = ReadFrameHeaderAsync(cancellationToken).GetAwaiter().GetResult();
                    
                    cancellationToken.ThrowIfCancellationRequested();
                    
                    var frameData = new byte[frameHeader.Size];
                    ReadExactAsync(frameData, 0, frameHeader.Size, cancellationToken).GetAwaiter().GetResult();

                    // Emit all frames (including config frames) - они нужны для инициализации декодера
                    _dispatcher.TryEnqueue(() =>
                    {
                        System.Diagnostics.Debug.WriteLine($"[CameraStream] Frame received: Size={frameData.Length}, Timestamp={frameHeader.Timestamp}, IsKeyFrame={frameHeader.IsKeyFrame}, IsConfig={frameHeader.IsConfig}");
                        FrameReceived?.Invoke(this, new FrameData
                        {
                            Data = frameData,
                            Timestamp = frameHeader.Timestamp,
                            IsKeyFrame = frameHeader.IsKeyFrame,
                            IsConfig = frameHeader.IsConfig
                        });
                    });
                }
                catch (OperationCanceledException)
                {
                    System.Diagnostics.Debug.WriteLine("[CameraStream] ReceiveLoop: Operation cancelled during read");
                    throw;
                }
                catch (EndOfStreamException ex)
                {
                    System.Diagnostics.Debug.WriteLine($"[CameraStream] ReceiveLoop: Stream ended: {ex.Message}");
                    break;
                }
                catch (IOException ex)
                {
                    // Соединение закрыто или произошла ошибка ввода-вывода
                    System.Diagnostics.Debug.WriteLine($"[CameraStream] ReceiveLoop: IO error (likely connection closed): {ex.Message}");
                    break;
                }
            }
            
            System.Diagnostics.Debug.WriteLine("[CameraStream] ReceiveLoop exiting normally");
        }
        catch (OperationCanceledException)
        {
            System.Diagnostics.Debug.WriteLine("[CameraStream] ReceiveLoop cancelled");
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[CameraStream] Receive loop error: {ex.Message}");
            if (!cancellationToken.IsCancellationRequested)
            {
                _dispatcher.TryEnqueue(async () =>
                {
                    await DisconnectAsync();
                });
            }
        }
        finally
        {
            System.Diagnostics.Debug.WriteLine("[CameraStream] ReceiveLoop completed");
        }
    }

    private static int ConvertFromBigEndianInt32(byte[] buffer, int offset)
    {
        if (BitConverter.IsLittleEndian)
        {
            return (buffer[offset] << 24) | (buffer[offset + 1] << 16) | (buffer[offset + 2] << 8) | buffer[offset + 3];
        }
        return BitConverter.ToInt32(buffer, offset);
    }

    private static long ConvertFromBigEndianInt64(byte[] buffer, int offset)
    {
        if (BitConverter.IsLittleEndian)
        {
            return ((long)buffer[offset] << 56) | ((long)buffer[offset + 1] << 48) | 
                   ((long)buffer[offset + 2] << 40) | ((long)buffer[offset + 3] << 32) |
                   ((long)buffer[offset + 4] << 24) | ((long)buffer[offset + 5] << 16) | 
                   ((long)buffer[offset + 6] << 8) | buffer[offset + 7];
        }
        return BitConverter.ToInt64(buffer, offset);
    }

    private void NotifyConnectionStateChanged(bool connected)
    {
        _dispatcher.TryEnqueue(() =>
        {
            ConnectionStateChanged?.Invoke(this, connected);
        });
    }
}






