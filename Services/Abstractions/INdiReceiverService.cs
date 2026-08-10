using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace ChyguiSlide.Services.Abstractions;

/// <summary>
/// Сервис для приема NDI видеопотока
/// </summary>
public interface INdiReceiverService
{
    /// <summary>
    /// Подключен ли к NDI источнику
    /// </summary>
    bool IsConnected { get; }
    
    /// <summary>
    /// Идет ли прием потока
    /// </summary>
    bool IsReceiving { get; }
    
    /// <summary>
    /// Событие изменения состояния подключения
    /// </summary>
    event EventHandler<bool>? ConnectionStateChanged;
    
    /// <summary>
    /// Событие получения нового видеокадра (в формате BGRA32)
    /// </summary>
    event EventHandler<NdiVideoFrame>? FrameReceived;
    
    /// <summary>
    /// Получить список доступных NDI источников в сети
    /// </summary>
    Task<List<NdiSource>> GetAvailableSourcesAsync();
    
    /// <summary>
    /// Подключиться к указанному NDI источнику
    /// </summary>
    Task ConnectAsync(string sourceName);
    
    /// <summary>
    /// Отключиться от текущего источника
    /// </summary>
    Task DisconnectAsync();
    
    /// <summary>
    /// Начать прием видеопотока
    /// </summary>
    Task StartReceivingAsync();
    
    /// <summary>
    /// Остановить прием видеопотока
    /// </summary>
    Task StopReceivingAsync();
}

/// <summary>
/// Информация об NDI источнике
/// </summary>
public class NdiSource
{
    public string Name { get; set; } = string.Empty;
    public string IpAddress { get; set; } = string.Empty;
}

/// <summary>
/// Видеокадр от NDI источника
/// </summary>
public class NdiVideoFrame
{
    /// <summary>
    /// Данные кадра в формате BGRA32 (4 байта на пиксель)
    /// </summary>
    public byte[] Data { get; set; } = Array.Empty<byte>();
    
    /// <summary>
    /// Ширина кадра в пикселях
    /// </summary>
    public int Width { get; set; }
    
    /// <summary>
    /// Высота кадра в пикселях
    /// </summary>
    public int Height { get; set; }
    
    /// <summary>
    /// Stride (количество байт на строку, обычно Width * 4 для BGRA32)
    /// </summary>
    public int Stride { get; set; }
    
    /// <summary>
    /// Временная метка кадра
    /// </summary>
    public long Timestamp { get; set; }
}


