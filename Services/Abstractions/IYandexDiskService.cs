using ChyguiSlide.Services.Models;

namespace ChyguiSlide.Services.Abstractions;

public interface IYandexDiskService
{
    Task<bool> ValidateTokenAsync(string token, CancellationToken cancellationToken = default);

    Task EnsureFolderAsync(string token, string folderPath, CancellationToken cancellationToken = default);

    Task UploadFileAsync(
        string token,
        string localFilePath,
        string remotePath,
        bool overwrite = true,
        CancellationToken cancellationToken = default);

    Task DownloadFileAsync(
        string token,
        string remotePath,
        string localFilePath,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<YandexDiskFileInfo>> ListFilesAsync(
        string token,
        string folderPath,
        CancellationToken cancellationToken = default);

    Task DeleteFileAsync(string token, string remotePath, CancellationToken cancellationToken = default);
}
