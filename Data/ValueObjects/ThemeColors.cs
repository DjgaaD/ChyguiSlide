namespace ChyguiSlide.Data.ValueObjects;

public record class ThemeColors(
    string Primary,
    string Background)
{
    public static ThemeColors Default { get; } = new("#FFFFFF", "#000000");
}

