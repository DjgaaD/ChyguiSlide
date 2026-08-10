namespace ChyguiSlide.Services.Models;

public sealed class AppUpdateInfo
{
    public required string Version { get; init; }

    public required string Channel { get; init; }

    public required string DisplayVersion { get; init; }

    public required string TagName { get; init; }

    public required string ReleaseName { get; init; }

    public required string Changelog { get; init; }

    public required Uri InstallerUrl { get; init; }

    public required string InstallerFileName { get; init; }

    public long? InstallerSizeBytes { get; init; }

    public DateTimeOffset? PublishedAt { get; init; }

    public bool IsMandatory { get; init; }
}
