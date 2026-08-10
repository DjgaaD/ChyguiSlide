namespace ChyguiSlide.Services.Abstractions;

/// <summary>Импорт фоновых медиафайлов стилей в локальное хранилище приложения.</summary>
public interface IThemeBackgroundMediaService
{
    string BackgroundsDirectory { get; }

    /// <summary>Копирует файл в хранилище приложения и возвращает абсолютный путь к копии.</summary>
    Task<string> ImportAsync(string sourceFilePath, CancellationToken cancellationToken = default);

    bool IsManagedPath(string? path);

    string GetDisplayName(string? path);

    /// <summary>Удаляет файл, если он лежит в хранилище приложения.</summary>
    void TryDeleteManaged(string? path);

    /// <summary>Возвращает существующий путь к файлу (управляемый или внешний legacy).</summary>
    string? ResolveExistingPath(string? storedPath);
}
