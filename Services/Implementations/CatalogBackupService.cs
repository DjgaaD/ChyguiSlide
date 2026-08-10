using System.Text.Json;
using ChyguiSlide.Data;
using ChyguiSlide.Services.Abstractions;
using ChyguiSlide.Services.Models;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace ChyguiSlide.Services.Implementations;

public sealed class CatalogBackupService : ICatalogBackupService
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };
    private readonly IYandexDiskService _disk;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly string _settingsPath;
    private readonly SemaphoreSlim _backupLock = new(1, 1);

    public CatalogBackupService(IYandexDiskService disk, IServiceScopeFactory scopeFactory)
    {
        _disk = disk;
        _scopeFactory = scopeFactory;
        var root = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "ChyguiSlide");
        Directory.CreateDirectory(root);
        _settingsPath = Path.Combine(root, "yandex-disk.json");
    }

    public async Task<YandexDiskSettings> GetSettingsAsync(CancellationToken cancellationToken = default)
    {
        if (!File.Exists(_settingsPath))
        {
            return new YandexDiskSettings();
        }

        await using var stream = File.OpenRead(_settingsPath);
        var settings = await JsonSerializer.DeserializeAsync<YandexDiskSettings>(stream, JsonOptions, cancellationToken)
            .ConfigureAwait(false);
        return settings ?? new YandexDiskSettings();
    }

    public async Task SaveSettingsAsync(YandexDiskSettings settings, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(settings);
        settings.AccessToken = settings.AccessToken.Trim();
        settings.ClientId = settings.ClientId.Trim();
        settings.Folder = string.IsNullOrWhiteSpace(settings.Folder)
            ? "ChyguiSlide-Backups"
            : settings.Folder.Trim().Trim('/');
        if (settings.MaxCopies < 1)
        {
            settings.MaxCopies = 1;
        }

        settings.AutoBackupHour = Math.Clamp(settings.AutoBackupHour, 0, 23);
        settings.AutoBackupMinute = Math.Clamp(settings.AutoBackupMinute, 0, 59);
        settings.AutoBackupDaysOfWeek = settings.GetEffectiveAutoBackupDays().ToList();
        // После нормализации старое поле больше не нужно
        settings.AutoBackupDayOfWeek = null;

        await using var stream = File.Create(_settingsPath);
        await JsonSerializer.SerializeAsync(stream, settings, JsonOptions, cancellationToken).ConfigureAwait(false);
    }

    public async Task<bool> TestConnectionAsync(CancellationToken cancellationToken = default)
    {
        var settings = await GetSettingsAsync(cancellationToken).ConfigureAwait(false);
        if (string.IsNullOrWhiteSpace(settings.AccessToken))
        {
            return false;
        }

        return await _disk.ValidateTokenAsync(settings.AccessToken, cancellationToken).ConfigureAwait(false);
    }

    public async Task<string> BackupToLocalFolderAsync(
        string folderPath,
        IProgress<string>? progress = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(folderPath);

        await _backupLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var dbPath = DatabasePathProvider.GetDatabasePath();
            if (!File.Exists(dbPath))
            {
                throw new FileNotFoundException("База песен не найдена.", dbPath);
            }

            Directory.CreateDirectory(folderPath);
            var stamp = DateTime.Now.ToString("yyyyMMdd-HHmmss");
            var fileName = $"catalog-{stamp}.db";
            var destFile = Path.Combine(folderPath, fileName);

            progress?.Report("Создание снимка базы…");
            await CreateSqliteSnapshotAsync(dbPath, destFile, cancellationToken).ConfigureAwait(false);
            progress?.Report("Готово.");
            return destFile;
        }
        finally
        {
            _backupLock.Release();
        }
    }

    public async Task<string> BackupToYandexDiskAsync(
        IProgress<string>? progress = null,
        CancellationToken cancellationToken = default)
    {
        await _backupLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var settings = await RequireTokenAsync(cancellationToken).ConfigureAwait(false);
            var dbPath = DatabasePathProvider.GetDatabasePath();
            if (!File.Exists(dbPath))
            {
                throw new FileNotFoundException("База песен не найдена.", dbPath);
            }

            var tempDir = Path.Combine(Path.GetTempPath(), "ChyguiSlide-Backup");
            Directory.CreateDirectory(tempDir);
            var stamp = DateTime.Now.ToString("yyyyMMdd-HHmmss");
            var fileName = $"catalog-{stamp}.db";
            var tempFile = Path.Combine(tempDir, fileName);

            try
            {
                progress?.Report("Создание снимка базы…");
                await CreateSqliteSnapshotAsync(dbPath, tempFile, cancellationToken).ConfigureAwait(false);

                var remotePath = $"{settings.Folder}/{fileName}";
                progress?.Report("Загрузка на Яндекс.Диск…");
                await _disk.UploadFileAsync(settings.AccessToken, tempFile, remotePath, true, cancellationToken)
                    .ConfigureAwait(false);

                progress?.Report("Очистка старых копий…");
                await CleanupOldBackupsAsync(settings, cancellationToken).ConfigureAwait(false);

                progress?.Report("Готово.");
                return fileName;
            }
            finally
            {
                SqliteConnection.ClearAllPools();
                await Task.Delay(100, CancellationToken.None).ConfigureAwait(false);
                TryDelete(tempFile);
                TryDelete(tempFile + "-wal");
                TryDelete(tempFile + "-shm");
            }
        }
        finally
        {
            _backupLock.Release();
        }
    }

    public async Task<IReadOnlyList<YandexDiskFileInfo>> ListBackupsAsync(CancellationToken cancellationToken = default)
    {
        var settings = await RequireTokenAsync(cancellationToken).ConfigureAwait(false);
        var files = await _disk.ListFilesAsync(settings.AccessToken, settings.Folder, cancellationToken)
            .ConfigureAwait(false);
        return files
            .Where(f => f.Name.EndsWith(".db", StringComparison.OrdinalIgnoreCase)
                        || f.Name.EndsWith(".sqlite", StringComparison.OrdinalIgnoreCase))
            .ToList();
    }

    public async Task RestoreFromYandexDiskAsync(
        string remotePath,
        IProgress<string>? progress = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(remotePath);
        var settings = await RequireTokenAsync(cancellationToken).ConfigureAwait(false);
        var dbPath = DatabasePathProvider.GetDatabasePath();
        var tempDir = Path.Combine(Path.GetTempPath(), "ChyguiSlide-Backup");
        Directory.CreateDirectory(tempDir);
        var tempFile = Path.Combine(tempDir, $"restore-{Guid.NewGuid():N}.db");
        var backupBefore = Path.Combine(
            Path.GetDirectoryName(dbPath)!,
            $"catalog.before-restore-{DateTime.Now:yyyyMMdd-HHmmss}.db");

        try
        {
            progress?.Report("Скачивание копии…");
            await _disk.DownloadFileAsync(settings.AccessToken, remotePath, tempFile, cancellationToken)
                .ConfigureAwait(false);

            if (new FileInfo(tempFile).Length < 100)
            {
                throw new InvalidDataException("Скачанный файл слишком маленький — похоже, это не база.");
            }

            progress?.Report("Подготовка к замене базы…");
            await CheckpointAndReleaseAsync(cancellationToken).ConfigureAwait(false);

            if (File.Exists(dbPath))
            {
                File.Copy(dbPath, backupBefore, overwrite: true);
            }

            progress?.Report("Замена локальной базы…");
            File.Copy(tempFile, dbPath, overwrite: true);

            // WAL-хвосты от старой БД могут конфликтовать
            TryDelete(dbPath + "-wal");
            TryDelete(dbPath + "-shm");

            progress?.Report("Готово. Перезапустите приложение.");
        }
        finally
        {
            TryDelete(tempFile);
        }
    }

    private async Task CleanupOldBackupsAsync(YandexDiskSettings settings, CancellationToken cancellationToken)
    {
        var files = await ListBackupsAsync(cancellationToken).ConfigureAwait(false);
        if (files.Count <= settings.MaxCopies)
        {
            return;
        }

        foreach (var old in files.Skip(settings.MaxCopies))
        {
            try
            {
                await _disk.DeleteFileAsync(settings.AccessToken, old.Path, cancellationToken).ConfigureAwait(false);
            }
            catch
            {
                // не прерываем бэкап из‑за очистки
            }
        }
    }

    private async Task<YandexDiskSettings> RequireTokenAsync(CancellationToken cancellationToken)
    {
        var settings = await GetSettingsAsync(cancellationToken).ConfigureAwait(false);
        if (string.IsNullOrWhiteSpace(settings.AccessToken))
        {
            throw new InvalidOperationException(
                "Не задан токен Яндекс.Диска. Откройте Настройки → Резервные копии и вставьте OAuth-токен.");
        }

        return settings;
    }

    private async Task CreateSqliteSnapshotAsync(string dbPath, string destPath, CancellationToken cancellationToken)
    {
        await CheckpointAndReleaseAsync(cancellationToken).ConfigureAwait(false);
        TryDelete(destPath);
        TryDelete(destPath + "-wal");
        TryDelete(destPath + "-shm");

        // Отдельный файл-снимок; соединения обязательно закрыть до загрузки на Диск
        var source = new SqliteConnection($"Data Source={dbPath};Mode=ReadOnly");
        var dest = new SqliteConnection($"Data Source={destPath}");
        try
        {
            await source.OpenAsync(cancellationToken).ConfigureAwait(false);
            await dest.OpenAsync(cancellationToken).ConfigureAwait(false);
            source.BackupDatabase(dest);
        }
        finally
        {
            await dest.DisposeAsync().ConfigureAwait(false);
            await source.DisposeAsync().ConfigureAwait(false);
            SqliteConnection.ClearAllPools();
        }

        // Дать ОС отпустить хэндлы перед File.OpenRead при upload
        await Task.Delay(150, cancellationToken).ConfigureAwait(false);
    }

    private async Task CheckpointAndReleaseAsync(CancellationToken cancellationToken)
    {
        try
        {
            using var scope = _scopeFactory.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            await db.Database.ExecuteSqlRawAsync("PRAGMA wal_checkpoint(TRUNCATE);", cancellationToken)
                .ConfigureAwait(false);
        }
        catch
        {
            // БД может быть ещё не открыта
        }

        SqliteConnection.ClearAllPools();
        await Task.Delay(200, cancellationToken).ConfigureAwait(false);
    }

    private static void TryDelete(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch
        {
            // ignore
        }
    }
}
