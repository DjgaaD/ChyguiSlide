namespace ChyguiSlide.Services.Abstractions;

/// <summary>Медиафайлы быстрого плейлиста — по исходному пути, без копирования в AppData.</summary>
public interface IPlaylistMediaService
{
    /// <summary>Каталог hardlink/symlink для WebView (chygui.media).</summary>
    string MediaDirectory { get; }

    /// <summary>Проверяет файл и возвращает абсолютный исходный путь (без копирования).</summary>
    Task<string> ImportAsync(string sourceFilePath, CancellationToken cancellationToken = default);

    string GetDisplayName(string? path);

    string? ResolveExistingPath(string? storedPath);

    bool IsVideoPath(string? path);

    bool IsImagePath(string? path);

    /// <summary>
    /// True, если контейнер подходит для &lt;video&gt; в WebView2 (ISO BMFF/MP4 или WebM/MKV).
    /// Файлы с расширением .mp4, но внутри MPEG-TS и т.п. — false.
    /// </summary>
    bool IsWebViewPlayableVideo(string? path);
}
