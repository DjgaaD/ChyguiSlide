using System;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Web.WebView2.Core;

namespace ChyguiSlide.Windows;

/// <summary>
/// Отдаёт файл плейлиста в WebView2 без полной копии.
/// Virtual-host mapping фиксируется при PrepareWebViewAsync (chygui.media / chygui.backgrounds);
/// динамический Remap после NavigateToString WebView2 игнорирует — поэтому внешние файлы
/// подключаем hardlink/symlink в уже замапленную папку Media.
/// </summary>
internal static class PlaylistMediaWebHost
{
    public const string MediaHostName = "chygui.media";
    public const string BackgroundsHostName = "chygui.backgrounds";

    private static readonly object Gate = new();

    /// <summary>
    /// Возвращает https://chygui.media/... (или backgrounds) URL для &lt;video&gt;/&lt;img&gt;.
    /// Параметр core оставлен для совместимости вызовов; remap на CoreWebView2 не нужен.
    /// </summary>
    public static string? MapAndGetUrl(CoreWebView2? core, string? path)
    {
        _ = core;

        if (string.IsNullOrWhiteSpace(path))
        {
            return null;
        }

        try
        {
            var full = Path.GetFullPath(path);
            if (!File.Exists(full))
            {
                return null;
            }

            if (TryUrlUnderAppFolder(full, "Backgrounds", BackgroundsHostName, out var backgroundsUrl))
            {
                return backgroundsUrl;
            }

            if (TryUrlUnderAppFolder(full, "Media", MediaHostName, out var mediaUrl))
            {
                return mediaUrl;
            }

            var linkFileName = EnsureMediaLink(full);
            if (string.IsNullOrWhiteSpace(linkFileName))
            {
                return null;
            }

            return $"https://{MediaHostName}/{Uri.EscapeDataString(linkFileName)}";
        }
        catch (Exception ex)
        {
            ChyguiSlide.Data.InteractionLogger.Log(
                $"[PlaylistMediaWebHost] MapAndGetUrl failed: {ex.Message}");
            return null;
        }
    }

    private static bool TryUrlUnderAppFolder(
        string fullPath,
        string folderName,
        string hostName,
        out string? url)
    {
        url = null;
        var root = Path.GetFullPath(Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "ChyguiSlide",
                folderName))
            .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
            + Path.DirectorySeparatorChar;

        if (!fullPath.StartsWith(root, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var relative = fullPath[root.Length..].Replace('\\', '/');
        var encoded = string.Join('/',
            relative.Split('/', StringSplitOptions.RemoveEmptyEntries)
                .Select(Uri.EscapeDataString));
        url = $"https://{hostName}/{encoded}";
        return true;
    }

    private static string MediaDirectory => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "ChyguiSlide",
        "Media");

    /// <summary>
    /// Создаёт hardlink (или symlink) в Media с ASCII-именем; без дублирования байтов на том же томе.
    /// </summary>
    private static string? EnsureMediaLink(string fullPath)
    {
        lock (Gate)
        {
            Directory.CreateDirectory(MediaDirectory);

            var ext = Path.GetExtension(fullPath);
            if (string.IsNullOrWhiteSpace(ext))
            {
                ext = ".bin";
            }

            var hash = Convert.ToHexString(
                    SHA256.HashData(Encoding.UTF8.GetBytes(fullPath.ToUpperInvariant())))
                .ToLowerInvariant()[..32];
            var linkName = hash + ext.ToLowerInvariant();
            var linkPath = Path.Combine(MediaDirectory, linkName);

            if (File.Exists(linkPath))
            {
                try
                {
                    var linkInfo = new FileInfo(linkPath);
                    var srcInfo = new FileInfo(fullPath);
                    if (linkInfo.Length == srcInfo.Length)
                    {
                        return linkName;
                    }

                    // Сломанный/чужой линк — пересоздаём.
                    File.Delete(linkPath);
                }
                catch
                {
                    try { File.Delete(linkPath); } catch { /* ignore */ }
                }
            }

            if (TryCreateHardLink(linkPath, fullPath))
            {
                ChyguiSlide.Data.InteractionLogger.Log(
                    $"[PlaylistMediaWebHost] hardlink {linkName} ← {fullPath}");
                return linkName;
            }

            if (TryCreateSymbolicLink(linkPath, fullPath))
            {
                ChyguiSlide.Data.InteractionLogger.Log(
                    $"[PlaylistMediaWebHost] symlink {linkName} ← {fullPath}");
                return linkName;
            }

            // Cross-volume / no Developer Mode: one-time copy into Media so WebView still plays.
            if (TryCopyMediaFile(linkPath, fullPath))
            {
                ChyguiSlide.Data.InteractionLogger.Log(
                    $"[PlaylistMediaWebHost] copy fallback {linkName} ← {fullPath}");
                return linkName;
            }

            ChyguiSlide.Data.InteractionLogger.LogError(
                $"[PlaylistMediaWebHost] link+copy failed for {fullPath}. " +
                "Файл на другом томе? Включите режим разработчика Windows для symlink.");
            return null;
        }
    }

    private static bool TryCopyMediaFile(string destPath, string sourcePath)
    {
        try
        {
            if (File.Exists(destPath))
            {
                File.Delete(destPath);
            }

            File.Copy(sourcePath, destPath, overwrite: false);
            return File.Exists(destPath);
        }
        catch (Exception ex)
        {
            ChyguiSlide.Data.InteractionLogger.Log(
                $"[PlaylistMediaWebHost] File.Copy failed: {ex.Message}");
            try
            {
                if (File.Exists(destPath))
                {
                    File.Delete(destPath);
                }
            }
            catch
            {
                /* ignore */
            }

            return false;
        }
    }

    private static bool TryCreateHardLink(string linkPath, string targetPath)
    {
        try
        {
            if (CreateHardLink(linkPath, targetPath, IntPtr.Zero))
            {
                return true;
            }

            var err = Marshal.GetLastWin32Error();
            ChyguiSlide.Data.InteractionLogger.Log(
                $"[PlaylistMediaWebHost] CreateHardLink Win32={err}");
        }
        catch (Exception ex)
        {
            ChyguiSlide.Data.InteractionLogger.Log(
                $"[PlaylistMediaWebHost] CreateHardLink: {ex.Message}");
        }

        return false;
    }

    private static bool TryCreateSymbolicLink(string linkPath, string targetPath)
    {
        try
        {
            // 0 = file, 2 = ALLOW_UNPRIVILEGED_CREATE (Developer Mode)
            const int fileLink = 0x0;
            const int allowUnprivileged = 0x2;
            if (CreateSymbolicLink(linkPath, targetPath, fileLink | allowUnprivileged)
                || CreateSymbolicLink(linkPath, targetPath, fileLink))
            {
                return File.Exists(linkPath);
            }

            var err = Marshal.GetLastWin32Error();
            ChyguiSlide.Data.InteractionLogger.Log(
                $"[PlaylistMediaWebHost] CreateSymbolicLink Win32={err}");
        }
        catch (Exception ex)
        {
            ChyguiSlide.Data.InteractionLogger.Log(
                $"[PlaylistMediaWebHost] CreateSymbolicLink: {ex.Message}");
        }

        return false;
    }

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool CreateHardLink(
        string lpFileName,
        string lpExistingFileName,
        IntPtr lpSecurityAttributes);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool CreateSymbolicLink(
        string lpSymlinkFileName,
        string lpTargetFileName,
        int dwFlags);
}
