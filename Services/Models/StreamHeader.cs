namespace ChyguiSlide.Services.Models;

public struct StreamHeader
{
    public byte[] Magic; // "GRST" (4 bytes)
    public int Version;
    public long Reserved;
}






