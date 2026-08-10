namespace ChyguiSlide.Services.Models;

public struct FrameHeader
{
    public int Size;       // Frame data size in bytes
    public long Timestamp; // Presentation timestamp in microseconds
    public int Flags;      // Frame flags (bit 0: keyframe, bit 1: config)
    
    public bool IsKeyFrame => (Flags & 0x01) != 0;
    public bool IsConfig => (Flags & 0x02) != 0;
}






