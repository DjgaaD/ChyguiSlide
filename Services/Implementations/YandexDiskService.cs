using System.Net.Http.Headers;
using System.Text.Json;
using System.Text.Json.Serialization;
using ChyguiSlide.Services.Abstractions;
using ChyguiSlide.Services.Models;

namespace ChyguiSlide.Services.Implementations;

/// <summary>Клиент API Яндекс.Диска (как в CRM: cloud-api.yandex.net).</summary>
public sealed class YandexDiskService : IYandexDiskService
{
    private const string ApiBase = "https://cloud-api.yandex.net/v1/disk";
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    private readonly HttpClient _http = new()
    {
        Timeout = TimeSpan.FromMinutes(10)
    };

    public async Task<bool> ValidateTokenAsync(string token, CancellationToken cancellationToken = default)
    {
        token = token.Trim();
        if (token.Length < 10)
        {
            return false;
        }

        using var response = await SendAsync(HttpMethod.Get, "/", token, cancellationToken).ConfigureAwait(false);
        return response.IsSuccessStatusCode;
    }

    public async Task EnsureFolderAsync(string token, string folderPath, CancellationToken cancellationToken = default)
    {
        folderPath = NormalizeDiskPath(folderPath);
        using var get = await SendAsync(
            HttpMethod.Get,
            $"/resources?path={Uri.EscapeDataString(folderPath)}",
            token,
            cancellationToken).ConfigureAwait(false);

        if (get.IsSuccessStatusCode)
        {
            return;
        }

        if ((int)get.StatusCode != 404)
        {
            throw await ToExceptionAsync(get, "Не удалось проверить папку на Яндекс.Диске").ConfigureAwait(false);
        }

        using var put = await SendAsync(
            HttpMethod.Put,
            $"/resources?path={Uri.EscapeDataString(folderPath)}",
            token,
            cancellationToken).ConfigureAwait(false);

        if (!put.IsSuccessStatusCode && (int)put.StatusCode != 409)
        {
            throw await ToExceptionAsync(put, "Не удалось создать папку на Яндекс.Диске").ConfigureAwait(false);
        }
    }

    public async Task UploadFileAsync(
        string token,
        string localFilePath,
        string remotePath,
        bool overwrite = true,
        CancellationToken cancellationToken = default)
    {
        if (!File.Exists(localFilePath))
        {
            throw new FileNotFoundException("Локальный файл не найден.", localFilePath);
        }

        remotePath = NormalizeDiskPath(remotePath);
        var slash = remotePath.LastIndexOf('/');
        if (slash > 0)
        {
            await EnsureFolderAsync(token, remotePath[..slash], cancellationToken).ConfigureAwait(false);
        }

        using var uploadLinkResponse = await SendAsync(
            HttpMethod.Get,
            $"/resources/upload?path={Uri.EscapeDataString(remotePath)}&overwrite={(overwrite ? "true" : "false")}",
            token,
            cancellationToken).ConfigureAwait(false);

        if (!uploadLinkResponse.IsSuccessStatusCode)
        {
            throw await ToExceptionAsync(uploadLinkResponse, "Не удалось получить ссылку для загрузки").ConfigureAwait(false);
        }

        var linkJson = await uploadLinkResponse.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
        var link = JsonSerializer.Deserialize<HrefResponse>(linkJson, JsonOptions);
        if (string.IsNullOrWhiteSpace(link?.Href))
        {
            throw new InvalidOperationException("Яндекс.Диск не вернул URL для загрузки.");
        }

        await using var stream = new MemoryStream(await File.ReadAllBytesAsync(localFilePath, cancellationToken).ConfigureAwait(false));
        using var content = new StreamContent(stream);
        content.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");

        using var put = await _http.PutAsync(link.Href, content, cancellationToken).ConfigureAwait(false);
        if (!put.IsSuccessStatusCode)
        {
            var body = await put.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
            throw new InvalidOperationException($"Ошибка загрузки на Яндекс.Диск ({(int)put.StatusCode}): {body}");
        }
    }

    public async Task DownloadFileAsync(
        string token,
        string remotePath,
        string localFilePath,
        CancellationToken cancellationToken = default)
    {
        remotePath = NormalizeDiskPath(remotePath);
        using var linkResponse = await SendAsync(
            HttpMethod.Get,
            $"/resources/download?path={Uri.EscapeDataString(remotePath)}",
            token,
            cancellationToken).ConfigureAwait(false);

        if (!linkResponse.IsSuccessStatusCode)
        {
            throw await ToExceptionAsync(linkResponse, "Не удалось получить ссылку для скачивания").ConfigureAwait(false);
        }

        var linkJson = await linkResponse.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
        var link = JsonSerializer.Deserialize<HrefResponse>(linkJson, JsonOptions);
        if (string.IsNullOrWhiteSpace(link?.Href))
        {
            throw new InvalidOperationException("Яндекс.Диск не вернул URL для скачивания.");
        }

        using var download = await _http.GetAsync(link.Href, HttpCompletionOption.ResponseHeadersRead, cancellationToken)
            .ConfigureAwait(false);
        if (!download.IsSuccessStatusCode)
        {
            var body = await download.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
            throw new InvalidOperationException($"Ошибка скачивания ({(int)download.StatusCode}): {body}");
        }

        var dir = Path.GetDirectoryName(localFilePath);
        if (!string.IsNullOrEmpty(dir))
        {
            Directory.CreateDirectory(dir);
        }

        await using var input = await download.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        await using var output = File.Create(localFilePath);
        await input.CopyToAsync(output, cancellationToken).ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<YandexDiskFileInfo>> ListFilesAsync(
        string token,
        string folderPath,
        CancellationToken cancellationToken = default)
    {
        folderPath = NormalizeDiskPath(folderPath);
        using var response = await SendAsync(
            HttpMethod.Get,
            $"/resources?path={Uri.EscapeDataString(folderPath)}&limit=1000",
            token,
            cancellationToken).ConfigureAwait(false);

        if ((int)response.StatusCode == 404)
        {
            return Array.Empty<YandexDiskFileInfo>();
        }

        if (!response.IsSuccessStatusCode)
        {
            throw await ToExceptionAsync(response, "Не удалось получить список файлов").ConfigureAwait(false);
        }

        var json = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
        var root = JsonSerializer.Deserialize<ResourceListResponse>(json, JsonOptions);
        var items = root?.Embedded?.Items ?? Array.Empty<ResourceItem>();

        return items
            .Where(i => string.Equals(i.Type, "file", StringComparison.OrdinalIgnoreCase))
            .Select(i => new YandexDiskFileInfo
            {
                Name = i.Name ?? Path.GetFileName(i.Path ?? string.Empty),
                Path = NormalizeDiskPath(i.Path ?? string.Empty),
                Size = i.Size,
                Modified = i.Modified,
                Created = i.Created
            })
            .OrderByDescending(f => f.Modified ?? f.Created ?? DateTimeOffset.MinValue)
            .ToList();
    }

    public async Task DeleteFileAsync(string token, string remotePath, CancellationToken cancellationToken = default)
    {
        remotePath = NormalizeDiskPath(remotePath);
        using var response = await SendAsync(
            HttpMethod.Delete,
            $"/resources?path={Uri.EscapeDataString(remotePath)}",
            token,
            cancellationToken).ConfigureAwait(false);

        if (!response.IsSuccessStatusCode && (int)response.StatusCode != 404)
        {
            throw await ToExceptionAsync(response, "Не удалось удалить файл на Яндекс.Диске").ConfigureAwait(false);
        }
    }

    private async Task<HttpResponseMessage> SendAsync(
        HttpMethod method,
        string endpoint,
        string token,
        CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(method, ApiBase + endpoint);
        request.Headers.TryAddWithoutValidation("Authorization", $"OAuth {token.Trim()}");
        return await _http.SendAsync(request, cancellationToken).ConfigureAwait(false);
    }

    private static string NormalizeDiskPath(string path)
    {
        path = path.Trim().Replace('\\', '/');
        if (path.StartsWith("disk:", StringComparison.OrdinalIgnoreCase))
        {
            path = path[5..];
        }

        while (path.StartsWith('/'))
        {
            path = path[1..];
        }

        return path;
    }

    private static async Task<Exception> ToExceptionAsync(HttpResponseMessage response, string prefix)
    {
        var body = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
        return new InvalidOperationException($"{prefix} ({(int)response.StatusCode}): {body}");
    }

    private sealed class HrefResponse
    {
        [JsonPropertyName("href")]
        public string? Href { get; set; }
    }

    private sealed class ResourceListResponse
    {
        [JsonPropertyName("_embedded")]
        public EmbeddedResources? Embedded { get; set; }
    }

    private sealed class EmbeddedResources
    {
        [JsonPropertyName("items")]
        public ResourceItem[]? Items { get; set; }
    }

    private sealed class ResourceItem
    {
        [JsonPropertyName("name")]
        public string? Name { get; set; }

        [JsonPropertyName("path")]
        public string? Path { get; set; }

        [JsonPropertyName("type")]
        public string? Type { get; set; }

        [JsonPropertyName("size")]
        public long Size { get; set; }

        [JsonPropertyName("modified")]
        public DateTimeOffset? Modified { get; set; }

        [JsonPropertyName("created")]
        public DateTimeOffset? Created { get; set; }
    }
}
