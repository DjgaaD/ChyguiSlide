namespace ChyguiSlide.Services.Models;

public class FrameData
{
    public byte[] Data { get; set; } = Array.Empty<byte>();
    public long Timestamp { get; set; }
    public bool IsKeyFrame { get; set; }
    public bool IsConfig { get; set; }
}






