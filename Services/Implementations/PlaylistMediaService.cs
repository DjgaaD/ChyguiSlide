using ChyguiSlide.Services.Abstractions;

namespace ChyguiSlide.Services.Implementations;

public sealed class PlaylistMediaService : IPlaylistMediaService
{
    private static readonly HashSet<string> ImageExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".jpg", ".jpeg", ".png", ".bmp", ".gif", ".webp"
    };

    private static readonly HashSet<string> VideoExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".mp4", ".mkv", ".avi"
    };

    public PlaylistMediaService()
    {
        MediaDirectory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "ChyguiSlide",
            "Media");
        Directory.CreateDirectory(MediaDirectory);
    }

    public string MediaDirectory { get; }

    public Task<string> ImportAsync(string sourceFilePath, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sourceFilePath);

        if (!File.Exists(sourceFilePath))
        {
            throw new FileNotFoundException("Медиафайл не найден.", sourceFilePath);
        }

        var extension = Path.GetExtension(sourceFilePath);
        if (string.IsNullOrWhiteSpace(extension)
            || (!ImageExtensions.Contains(extension) && !VideoExtensions.Contains(extension)))
        {
            throw new InvalidOperationException(
                "Поддерживаются изображения (JPG, PNG, BMP, GIF, WebP) и видео (MP4, MKV, AVI).");
        }

        // Без копии: храним исходный путь и играем оттуда (большие файлы не дублируем).
        var full = Path.GetFullPath(sourceFilePath);
        return Task.FromResult(full);
    }

    public string GetDisplayName(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return string.Empty;
        }

        return Path.GetFileName(path);
    }

    public string? ResolveExistingPath(string? storedPath)
    {
        if (string.IsNullOrWhiteSpace(storedPath))
        {
            return null;
        }

        try
        {
            var full = Path.GetFullPath(storedPath);
            return File.Exists(full) ? full : null;
        }
        catch
        {
            return null;
        }
    }

    public bool IsVideoPath(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return false;
        }

        return VideoExtensions.Contains(Path.GetExtension(path));
    }

    public bool IsImagePath(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return false;
        }

        return ImageExtensions.Contains(Path.GetExtension(path));
    }

    public bool IsWebViewPlayableVideo(string? path)
    {
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
        {
            return false;
        }

        try
        {
            using var stream = File.OpenRead(path);
            Span<byte> header = stackalloc byte[16];
            var read = stream.Read(header);
            if (read < 8)
            {
                return false;
            }

            // WebM / Matroska EBML
            if (header[0] == 0x1A && header[1] == 0x45 && header[2] == 0xDF && header[3] == 0xA3)
            {
                return true;
            }

            // ISO BMFF (MP4/M4V/MOV): size(4) + 'ftyp'
            if (header[4] == (byte)'f'
                && header[5] == (byte)'t'
                && header[6] == (byte)'y'
                && header[7] == (byte)'p')
            {
                return true;
            }

            return false;
        }
        catch
        {
            return false;
        }
    }
}
