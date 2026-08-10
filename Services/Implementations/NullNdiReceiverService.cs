using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using ChyguiSlide.Services.Abstractions;

namespace ChyguiSlide.Services.Implementations;

/// <summary>
/// Пустой (no-op) сервис NDI.
/// Используется, когда NDI runtime/зависимости не установлены,
/// чтобы приложение не падало при открытии страниц Settings/Live.
/// </summary>
public sealed class NullNdiReceiverService : INdiReceiverService
{
    public bool IsConnected => false;
    public bool IsReceiving => false;

    public event EventHandler<bool>? ConnectionStateChanged;
    public event EventHandler<NdiVideoFrame>? FrameReceived;

    public Task<List<NdiSource>> GetAvailableSourcesAsync()
        => Task.FromResult(new List<NdiSource>());

    public Task ConnectAsync(string sourceName)
        => Task.CompletedTask;

    public Task DisconnectAsync()
        => Task.CompletedTask;

    public Task StartReceivingAsync()
        => Task.CompletedTask;

    public Task StopReceivingAsync()
        => Task.CompletedTask;
}

