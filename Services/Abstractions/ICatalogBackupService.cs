using ChyguiSlide.Services.Models;

namespace ChyguiSlide.Services.Abstractions;

public interface ICatalogBackupService
{
    Task<YandexDiskSettings> GetSettingsAsync(CancellationToken cancellationToken = default);
    Task SaveSettingsAsync(YandexDiskSettings settings, CancellationToken cancellationToken = default);

    Task<bool> TestConnectionAsync(CancellationToken cancellationToken = default);

    /// <summary>Снимок catalog.db → Яндекс.Диск. Возвращает имя загруженного файла.</summary>
    Task<string> BackupToYandexDiskAsync(
        IProgress<string>? progress = null,
        CancellationToken cancellationToken = default);

    /// <summary>Снимок catalog.db в указанную папку. Возвращает полный путь к файлу.</summary>
    Task<string> BackupToLocalFolderAsync(
        string folderPath,
        IProgress<string>? progress = null,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<YandexDiskFileInfo>> ListBackupsAsync(CancellationToken cancellationToken = default);

    /// <summary>Скачать копию и заменить локальную БД. После вызова нужен перезапуск приложения.</summary>
    Task RestoreFromYandexDiskAsync(
        string remotePath,
        IProgress<string>? progress = null,
        CancellationToken cancellationToken = default);
}
