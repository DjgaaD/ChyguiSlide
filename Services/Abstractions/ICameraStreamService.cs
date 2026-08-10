using ChyguiSlide.Services.Models;

namespace ChyguiSlide.Services.Abstractions;

public interface ICameraStreamService
{
    bool IsConnected { get; }
    bool IsStreaming { get; }
    
    event EventHandler<bool>? ConnectionStateChanged;
    event EventHandler<FrameData>? FrameReceived;
    
    Task ConnectAsync(string host, int port);
    Task DisconnectAsync();
    Task StartStreamingAsync();
    Task StopStreamingAsync();
}






