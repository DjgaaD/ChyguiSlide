using ChyguiSlide.Data.Entities;
using ChyguiSlide.Data.Enums;

namespace ChyguiSlide.Services;

/// <summary>Выбор пути к обоям стиля с учётом пула и режима Fixed/Random.</summary>
public static class ThemeBackgroundResolver
{
    public static ThemeWallpaperPool ResolvePool(ThemePreset theme, bool isBibleContent)
    {
        if (!theme.UseSeparateBackgrounds)
        {
            return ThemeWallpaperPool.Shared;
        }

        return isBibleContent ? ThemeWallpaperPool.Bible : ThemeWallpaperPool.Songs;
    }

    public static Guid? GetSelectedId(ThemePreset theme, ThemeWallpaperPool pool) =>
        pool switch
        {
            ThemeWallpaperPool.Songs => theme.SelectedSongWallpaperId,
            ThemeWallpaperPool.Bible => theme.SelectedBibleWallpaperId,
            _ => theme.SelectedSharedWallpaperId
        };

    public static void SetSelectedId(ThemePreset theme, ThemeWallpaperPool pool, Guid? id)
    {
        switch (pool)
        {
            case ThemeWallpaperPool.Songs:
                theme.SelectedSongWallpaperId = id;
                break;
            case ThemeWallpaperPool.Bible:
                theme.SelectedBibleWallpaperId = id;
                break;
            default:
                theme.SelectedSharedWallpaperId = id;
                break;
        }
    }

    public static IReadOnlyList<ThemeWallpaper> GetPoolItems(ThemePreset theme, ThemeWallpaperPool pool)
    {
        var wallpapers = theme.Wallpapers ?? Array.Empty<ThemeWallpaper>();
        var items = wallpapers
            .Where(w => w.Pool == pool)
            .OrderBy(w => w.SortOrder)
            .ThenBy(w => w.DisplayName, StringComparer.CurrentCultureIgnoreCase)
            .ToList();

        if (items.Count == 0 && pool != ThemeWallpaperPool.Shared)
        {
            items = wallpapers
                .Where(w => w.Pool == ThemeWallpaperPool.Shared)
                .OrderBy(w => w.SortOrder)
                .ThenBy(w => w.DisplayName, StringComparer.CurrentCultureIgnoreCase)
                .ToList();
        }

        return items;
    }

    /// <summary>
    /// Возвращает путь к фону. Для Random при новом запуске/пуле пишет выбор в <paramref name="sessionPicks"/>.
    /// </summary>
    public static string? ResolvePath(
        ThemePreset? theme,
        bool isBibleContent,
        IDictionary<ThemeWallpaperPool, Guid> sessionPicks,
        bool forceNewRandom)
    {
        if (theme is null)
        {
            return null;
        }

        var pool = ResolvePool(theme, isBibleContent);
        var items = GetPoolItems(theme, pool);

        if (items.Count == 0)
        {
            return string.IsNullOrWhiteSpace(theme.BackgroundMediaPath)
                ? null
                : theme.BackgroundMediaPath.Trim();
        }

        ThemeWallpaper? chosen = null;

        if (theme.BackgroundPickMode == ThemeBackgroundPickMode.RandomOnStart)
        {
            if (forceNewRandom || !sessionPicks.TryGetValue(pool, out var sessionId))
            {
                chosen = items[Random.Shared.Next(items.Count)];
                sessionPicks[pool] = chosen.Id;
            }
            else
            {
                chosen = items.FirstOrDefault(w => w.Id == sessionId) ?? items[Random.Shared.Next(items.Count)];
                sessionPicks[pool] = chosen.Id;
            }
        }
        else
        {
            var selectedId = GetSelectedId(theme, pool);
            chosen = items.FirstOrDefault(w => w.Id == selectedId) ?? items[0];
        }

        return string.IsNullOrWhiteSpace(chosen.FilePath) ? null : chosen.FilePath.Trim();
    }
}
