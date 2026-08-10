using System.Diagnostics;
using System.Globalization;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Text.RegularExpressions;
using ChyguiSlide.Services.Abstractions;
using ChyguiSlide.Services.Models;

namespace ChyguiSlide.Services.Implementations;

public sealed class AppUpdateService : IAppUpdateService, IDisposable
{
    private const string GitHubOwner = "DjgaaD";
    private const string GitHubRepo = "ChyguiSlide";
    private static readonly Uri ReleasesApi =
        new($"https://api.github.com/repos/{GitHubOwner}/{GitHubRepo}/releases");

    private readonly HttpClient _http;

    public AppUpdateService()
    {
        _http = new HttpClient
        {
            Timeout = TimeSpan.FromMinutes(10)
        };
        _http.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("ChyguiSlide", AppVersionInfo.Version));
        _http.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));
    }

    public async Task<AppUpdateInfo?> CheckForUpdateAsync(CancellationToken cancellationToken = default)
    {
        using var response = await _http.GetAsync(ReleasesApi, cancellationToken).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
        {
            System.Diagnostics.Debug.WriteLine(
                $"[Update] GitHub API {(int)response.StatusCode}: {response.ReasonPhrase}");
            return null;
        }

        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        using var doc = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken).ConfigureAwait(false);
        if (doc.RootElement.ValueKind != JsonValueKind.Array)
        {
            return null;
        }

        var channel = AppVersionInfo.Channel;
        var current = ParseVersion(AppVersionInfo.Version);
        if (current is null)
        {
            return null;
        }

        AppUpdateInfo? best = null;
        Version? bestVersion = null;

        foreach (var release in doc.RootElement.EnumerateArray())
        {
            if (release.TryGetProperty("draft", out var draft) && draft.GetBoolean())
            {
                continue;
            }

            var tag = release.TryGetProperty("tag_name", out var tagEl) ? tagEl.GetString() ?? "" : "";
            var isPrerelease = release.TryGetProperty("prerelease", out var pre) && pre.GetBoolean();
            if (!MatchesChannel(channel, tag, isPrerelease))
            {
                continue;
            }

            var remoteVersion = ExtractVersion(tag);
            if (remoteVersion is null || remoteVersion <= current)
            {
                continue;
            }

            if (!TryFindSetupAsset(release, out var assetUrl, out var assetName, out var assetSize))
            {
                continue;
            }

            if (bestVersion is not null && remoteVersion <= bestVersion)
            {
                continue;
            }

            var body = release.TryGetProperty("body", out var bodyEl) ? bodyEl.GetString() : null;
            var name = release.TryGetProperty("name", out var nameEl) ? nameEl.GetString() : null;
            DateTimeOffset? published = null;
            if (release.TryGetProperty("published_at", out var pubEl)
                && pubEl.ValueKind == JsonValueKind.String
                && DateTimeOffset.TryParse(pubEl.GetString(), CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var pub))
            {
                published = pub;
            }

            bestVersion = remoteVersion;
            best = new AppUpdateInfo
            {
                Version = remoteVersion.ToString(),
                Channel = channel,
                DisplayVersion = FormatDisplay(remoteVersion.ToString(), channel),
                TagName = tag,
                ReleaseName = string.IsNullOrWhiteSpace(name) ? tag : name!,
                Changelog = NormalizeChangelog(body),
                InstallerUrl = assetUrl,
                InstallerFileName = assetName,
                InstallerSizeBytes = assetSize,
                PublishedAt = published,
                IsMandatory = false
            };
        }

        return best;
    }

    public async Task<string> DownloadInstallerAsync(
        AppUpdateInfo update,
        IProgress<double>? progress = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(update);

        var tempDir = Path.Combine(Path.GetTempPath(), "ChyguiSlide-Updates");
        Directory.CreateDirectory(tempDir);
        var targetPath = Path.Combine(tempDir, update.InstallerFileName);

        using var response = await _http.GetAsync(update.InstallerUrl, HttpCompletionOption.ResponseHeadersRead, cancellationToken)
            .ConfigureAwait(false);
        response.EnsureSuccessStatusCode();

        var total = response.Content.Headers.ContentLength ?? update.InstallerSizeBytes;
        await using var source = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        await using var target = new FileStream(
            targetPath,
            FileMode.Create,
            FileAccess.Write,
            FileShare.None,
            81920,
            useAsync: true);

        var buffer = new byte[81920];
        long readTotal = 0;
        int read;
        while ((read = await source.ReadAsync(buffer.AsMemory(0, buffer.Length), cancellationToken).ConfigureAwait(false)) > 0)
        {
            await target.WriteAsync(buffer.AsMemory(0, read), cancellationToken).ConfigureAwait(false);
            readTotal += read;
            if (total is > 0)
            {
                progress?.Report(Math.Clamp(readTotal / (double)total.Value, 0, 1));
            }
        }

        progress?.Report(1);
        return targetPath;
    }

    public Process StartInstaller(string installerPath)
    {
        if (string.IsNullOrWhiteSpace(installerPath) || !File.Exists(installerPath))
        {
            throw new FileNotFoundException("Файл установщика не найден.", installerPath);
        }

        // Тихая установка поверх текущей; UAC покажет запрос прав администратора.
        // /MERGETASKS=!desktopicon — не создавать/не пересоздавать ярлык на рабочем столе.
        // Запуск приложения после установки — в Inno [Run] (без skipifsilent + runasoriginaluser).
        var args =
            "/SILENT /CLOSEAPPLICATIONS /FORCECLOSEAPPLICATIONS /NORESTART /SUPPRESSMSGBOXES " +
            "/MERGETASKS=\"!desktopicon\"";
        var psi = new ProcessStartInfo
        {
            FileName = installerPath,
            Arguments = args,
            UseShellExecute = true,
            Verb = "runas"
        };

        return Process.Start(psi)
               ?? throw new InvalidOperationException("Не удалось запустить установщик.");
    }

    public void Dispose() => _http.Dispose();

    private static bool MatchesChannel(string channel, string tag, bool isPrerelease)
    {
        var tagLower = tag.ToLowerInvariant();
        var isBetaTag = tagLower.Contains("beta", StringComparison.Ordinal)
                        || tagLower.Contains("alpha", StringComparison.Ordinal)
                        || tagLower.Contains("rc", StringComparison.Ordinal);

        if (string.Equals(channel, "beta", StringComparison.OrdinalIgnoreCase))
        {
            return isPrerelease || isBetaTag;
        }

        // release: только стабильные
        return !isPrerelease && !isBetaTag;
    }

    private static bool TryFindSetupAsset(
        JsonElement release,
        out Uri url,
        out string fileName,
        out long? size)
    {
        url = null!;
        fileName = "";
        size = null;

        if (!release.TryGetProperty("assets", out var assets) || assets.ValueKind != JsonValueKind.Array)
        {
            return false;
        }

        JsonElement? chosen = null;
        foreach (var item in assets.EnumerateArray())
        {
            var name = item.TryGetProperty("name", out var n) ? n.GetString() ?? "" : "";
            if (!name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (!name.Contains("Setup", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (name.Contains("ChyguiSlide", StringComparison.OrdinalIgnoreCase))
            {
                chosen = item;
                break;
            }

            chosen ??= item;
        }

        if (chosen is null)
        {
            return false;
        }

        var selected = chosen.Value;
        fileName = selected.GetProperty("name").GetString() ?? "ChyguiSlide-Setup.exe";
        var browserUrl = selected.TryGetProperty("browser_download_url", out var u) ? u.GetString() : null;
        if (string.IsNullOrWhiteSpace(browserUrl) || !Uri.TryCreate(browserUrl, UriKind.Absolute, out var parsed))
        {
            return false;
        }

        url = parsed;
        if (selected.TryGetProperty("size", out var sizeEl) && sizeEl.TryGetInt64(out var s))
        {
            size = s;
        }

        return true;
    }

    private static Version? ExtractVersion(string tag)
    {
        if (string.IsNullOrWhiteSpace(tag))
        {
            return null;
        }

        // v0.0.2-beta / 0.0.2-beta / v0.0.2
        var match = Regex.Match(tag, @"(\d+\.\d+\.\d+)");
        return match.Success ? ParseVersion(match.Groups[1].Value) : null;
    }

    private static Version? ParseVersion(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        return Version.TryParse(value.Trim(), out var v) ? v : null;
    }

    private static string FormatDisplay(string version, string channel) =>
        string.Equals(channel, "release", StringComparison.OrdinalIgnoreCase)
            ? version
            : $"{version} {channel}";

    private static string NormalizeChangelog(string? body)
    {
        if (string.IsNullOrWhiteSpace(body))
        {
            return "Список изменений не указан.";
        }

        var text = body.Replace("\r\n", "\n", StringComparison.Ordinal).Trim();
        if (text.Length > 4000)
        {
            text = text[..4000] + "\n…";
        }

        return text;
    }
}
