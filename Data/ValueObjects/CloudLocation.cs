namespace ChyguiSlide.Data.ValueObjects;

public record class CloudLocation(
    string Provider,
    string RemotePath,
    DateTime? SyncedAtUtc)
{
    public bool IsEmpty => string.IsNullOrWhiteSpace(Provider) || string.IsNullOrWhiteSpace(RemotePath);

    public static CloudLocation Empty { get; } = new(string.Empty, string.Empty, null);
}

