using System.Text;
using ChyguiSlide.Services.Abstractions;

namespace ChyguiSlide.Services.Implementations;

public sealed class ThemeBackgroundMediaService : IThemeBackgroundMediaService
{
    private static readonly HashSet<string> AllowedExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".jpg", ".jpeg", ".png", ".bmp", ".gif", ".webp",
        ".mp4", ".mov", ".wmv", ".mkv", ".avi", ".webm", ".m4v"
    };

    public ThemeBackgroundMediaService()
    {
        BackgroundsDirectory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "ChyguiSlide",
            "Backgrounds");
        Directory.CreateDirectory(BackgroundsDirectory);
    }

    public string BackgroundsDirectory { get; }

    public async Task<string> ImportAsync(string sourceFilePath, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sourceFilePath);

        if (!File.Exists(sourceFilePath))
        {
            throw new FileNotFoundException("Файл фона не найден.", sourceFilePath);
        }

        var extension = Path.GetExtension(sourceFilePath);
        if (string.IsNullOrWhiteSpace(extension) || !AllowedExtensions.Contains(extension))
        {
            throw new InvalidOperationException(
                "Поддерживаются изображения (JPG, PNG, BMP, GIF, WebP) и видео (MP4, MOV, WMV, MKV, AVI, WebM).");
        }

        Directory.CreateDirectory(BackgroundsDirectory);

        var safeBase = SanitizeFileName(Path.GetFileNameWithoutExtension(sourceFilePath));
        if (string.IsNullOrWhiteSpace(safeBase))
        {
            safeBase = "background";
        }

        var destName = safeBase + extension;
        var destPath = Path.Combine(BackgroundsDirectory, destName);
        if (File.Exists(destPath))
        {
            destName = $"{safeBase}-{Guid.NewGuid():N}{extension}";
            destPath = Path.Combine(BackgroundsDirectory, destName);
        }

        await using (var source = File.OpenRead(sourceFilePath))
        await using (var dest = File.Create(destPath))
        {
            await source.CopyToAsync(dest, cancellationToken).ConfigureAwait(false);
        }

        return destPath;
    }

    public bool IsManagedPath(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return false;
        }

        try
        {
            var full = Path.GetFullPath(path);
            var root = Path.GetFullPath(BackgroundsDirectory)
                .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
                + Path.DirectorySeparatorChar;
            return full.StartsWith(root, StringComparison.OrdinalIgnoreCase);
        }
        catch
        {
            return false;
        }
    }

    public string GetDisplayName(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return string.Empty;
        }

        return Path.GetFileName(path);
    }

    public void TryDeleteManaged(string? path)
    {
        if (!IsManagedPath(path) || string.IsNullOrWhiteSpace(path))
        {
            return;
        }

        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch
        {
            // Файл может быть занят плеером — не блокируем сохранение стиля.
        }
    }

    public string? ResolveExistingPath(string? storedPath)
    {
        if (string.IsNullOrWhiteSpace(storedPath))
        {
            return null;
        }

        try
        {
            if (Path.IsPathRooted(storedPath) && File.Exists(storedPath))
            {
                return storedPath;
            }

            var mediaDir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "ChyguiSlide",
                "Media");
            var byName = Path.Combine(mediaDir, Path.GetFileName(storedPath));
            if (File.Exists(byName))
            {
                return byName;
            }

            var byNameBackgrounds = Path.Combine(BackgroundsDirectory, Path.GetFileName(storedPath));
            if (File.Exists(byNameBackgrounds))
            {
                return byNameBackgrounds;
            }
        }
        catch
        {
            return null;
        }

        return null;
    }

    private static string SanitizeFileName(string name)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var sb = new StringBuilder(name.Length);
        foreach (var ch in name.Trim())
        {
            sb.Append(Array.IndexOf(invalid, ch) >= 0 ? '_' : ch);
        }

        var result = sb.ToString().Trim('.', ' ');
        return result.Length > 80 ? result[..80] : result;
    }
}
