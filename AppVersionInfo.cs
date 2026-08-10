using System.Reflection;

namespace ChyguiSlide;

/// <summary>Версия и канал сборки (beta / release), из метаданных сборки.</summary>
public static class AppVersionInfo
{
    static AppVersionInfo()
    {
        var assembly = typeof(AppVersionInfo).Assembly;
        var informational = assembly
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()
            ?.InformationalVersion
            ?.Trim();

        var fileVersion = assembly
            .GetCustomAttribute<AssemblyFileVersionAttribute>()
            ?.Version
            ?.Trim();

        Channel = assembly
            .GetCustomAttributes<AssemblyMetadataAttribute>()
            .FirstOrDefault(a => string.Equals(a.Key, "AppChannel", StringComparison.OrdinalIgnoreCase))
            ?.Value
            ?.Trim()
            ?? string.Empty;

        if (!string.IsNullOrWhiteSpace(informational))
        {
            // "0.0.1-beta" или "0.0.1+hash" → берём semver до '+'
            var withoutBuild = informational.Split('+', 2)[0];
            var dash = withoutBuild.IndexOf('-');
            if (dash > 0)
            {
                Version = withoutBuild[..dash];
                if (string.IsNullOrWhiteSpace(Channel))
                {
                    Channel = withoutBuild[(dash + 1)..];
                }
            }
            else
            {
                Version = withoutBuild;
            }
        }
        else if (!string.IsNullOrWhiteSpace(fileVersion))
        {
            // 0.0.1.0 → 0.0.1
            var parts = fileVersion.Split('.');
            Version = parts.Length >= 3
                ? string.Join('.', parts.Take(3))
                : fileVersion;
        }
        else
        {
            Version = assembly.GetName().Version?.ToString(3) ?? "0.0.0";
        }

        if (string.IsNullOrWhiteSpace(Channel))
        {
            Channel = "release";
        }
    }

    /// <summary>Семантическая версия без канала, например 0.0.1.</summary>
    public static string Version { get; }

    /// <summary>Канал: beta, release, …</summary>
    public static string Channel { get; }

    public static bool IsBeta =>
        string.Equals(Channel, "beta", StringComparison.OrdinalIgnoreCase);

    /// <summary>Для UI: «0.0.1 beta» или «1.0.0».</summary>
    public static string DisplayVersion =>
        IsBeta || (!string.Equals(Channel, "release", StringComparison.OrdinalIgnoreCase)
                   && !string.IsNullOrWhiteSpace(Channel))
            ? $"{Version} {Channel}"
            : Version;

    /// <summary>Полная метка: «ChyguiSlide 0.0.1 beta».</summary>
    public static string ProductLabel => $"ChyguiSlide {DisplayVersion}";
}
