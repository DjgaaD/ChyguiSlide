namespace ChyguiSlide.Services.Models;

public sealed class DisplayInfo
{
    public string Id { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public int Width { get; set; }
    public int Height { get; set; }
    public int X { get; set; }
    public int Y { get; set; }
    public bool IsPrimary { get; set; }

    public override string ToString() => Name;
}







