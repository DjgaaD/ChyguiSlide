using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using ChyguiSlide.Data;
using ChyguiSlide.Data.Entities;
using ChyguiSlide.Data.Enums;
using ChyguiSlide.Data.ValueObjects;
using ChyguiSlide.Services;
using ChyguiSlide.Services.Abstractions;
using ChyguiSlide.Services.Models;
using Microsoft.EntityFrameworkCore;

namespace ChyguiSlide.Services.Implementations;

public class CatalogService(AppDbContext dbContext) : ICatalogService
{
    private readonly AppDbContext _dbContext = dbContext;

    public async Task<IReadOnlyList<Song>> GetSongsAsync(CancellationToken cancellationToken = default)
    {
        var songs = await _dbContext.Songs
            .Include(song => song.Sections)
            .Include(song => song.SongTags)
            .ThenInclude(st => st.Tag)
            .Include(song => song.Collection)
            .OrderBy(song => song.Title)
            .AsNoTracking()
            .AsSplitQuery()
            .ToListAsync(cancellationToken);

        return songs
            .Select(song =>
            {
                song.Sections = song.Sections
                    .OrderBy(section => section.Order)
                    .ToList();
                return song;
            })
            .ToList();
    }

    public async Task<IReadOnlyList<Song>> SearchSongsAsync(string query, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(query))
        {
            return await GetSongsAsync(cancellationToken);
        }

        var normalizedQuery = NormalizeText(query);
        if (string.IsNullOrWhiteSpace(normalizedQuery))
        {
            return await GetSongsAsync(cancellationToken);
        }

        // Чистый номер — поиск по номеру песни
        var isNumberOnly = int.TryParse(normalizedQuery, out var parsedNumber);

        var songs = await _dbContext.Songs
            .Include(song => song.Sections)
            .Include(song => song.SongTags)
                .ThenInclude(st => st.Tag)
            .Include(song => song.Collection)
            .AsNoTracking()
            .AsSplitQuery()
            .ToListAsync(cancellationToken);

        var filtered = songs
            .Select(song =>
            {
                song.Sections = song.Sections
                    .OrderBy(section => section.Order)
                    .ToList();
                return song;
            })
            .Where(song =>
            {
                if (isNumberOnly)
                {
                    return song.Number == parsedNumber;
                }

                // Фраза целиком: слова подряд в том же порядке; хвост слова — префикс («пронзё»→«пронзёнными»)
                return SongMatchesPhrase(song, normalizedQuery);
            })
            .OrderBy(song => song.Title)
            .ToList();

        return filtered;
    }

    private static bool SongMatchesPhrase(Song song, string normalizedQuery)
    {
        foreach (var field in EnumerateSearchFields(song))
        {
            if (MatchesPhrase(NormalizeText(field), normalizedQuery))
            {
                return true;
            }
        }

        return false;
    }

    private static IEnumerable<string> EnumerateSearchFields(Song song)
    {
        if (!string.IsNullOrWhiteSpace(song.Title))
        {
            yield return song.Title;
        }

        if (!string.IsNullOrWhiteSpace(song.Subtitle))
        {
            yield return song.Subtitle;
        }

        if (song.Number is int number)
        {
            yield return number.ToString();
        }

        if (!string.IsNullOrWhiteSpace(song.Collection?.Name))
        {
            yield return song.Collection.Name;
        }

        if (song.SongTags?.Count > 0)
        {
            foreach (var tag in song.SongTags)
            {
                if (!string.IsNullOrWhiteSpace(tag.Tag?.Name))
                {
                    yield return tag.Tag.Name;
                }
            }
        }

        if (song.Sections?.Count > 0)
        {
            foreach (var section in song.Sections)
            {
                if (!string.IsNullOrWhiteSpace(section.Heading))
                {
                    yield return section.Heading;
                }

                if (!string.IsNullOrWhiteSpace(section.Content))
                {
                    // Каждая строка секции — отдельная фраза (не склеиваем весь текст в один мешок)
                    foreach (var line in section.Content.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries))
                    {
                        if (!string.IsNullOrWhiteSpace(line))
                        {
                            yield return line;
                        }
                    }
                }
            }
        }
    }

    private static string NormalizeText(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        var sb = new StringBuilder(value.Length);
        foreach (var ch in value)
        {
            var c = ch is 'ё' or 'Ё' ? 'е' : ch;
            if (char.IsLetterOrDigit(c))
            {
                sb.Append(char.ToLowerInvariant(c));
            }
            else if (char.IsWhiteSpace(c))
            {
                sb.Append(' ');
            }
            // пунктуация («,», «-», …) отбрасывается — «Мы, у берега» → «мы у берега»
        }

        return string.Join(' ', sb.ToString().Split(' ', StringSplitOptions.RemoveEmptyEntries));
    }

    /// <summary>
    /// Фраза: токены запроса совпадают с подряд идущими словами текста (порядок важен).
    /// Токен — префикс слова («пронзё» ⊂ «пронзёнными») или то же с 1 опечаткой при длине ≥ 4.
    /// </summary>
    private static bool MatchesPhrase(string normalizedField, string normalizedQuery)
    {
        if (string.IsNullOrWhiteSpace(normalizedField) || string.IsNullOrWhiteSpace(normalizedQuery))
        {
            return false;
        }

        var queryTokens = normalizedQuery.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var words = normalizedField.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (queryTokens.Length == 0 || words.Length < queryTokens.Length)
        {
            return false;
        }

        for (var start = 0; start <= words.Length - queryTokens.Length; start++)
        {
            var match = true;
            for (var i = 0; i < queryTokens.Length; i++)
            {
                if (!TokenMatchesWord(queryTokens[i], words[start + i]))
                {
                    match = false;
                    break;
                }
            }

            if (match)
            {
                return true;
            }
        }

        return false;
    }

    private static bool TokenMatchesWord(string token, string word)
    {
        if (word.StartsWith(token, StringComparison.Ordinal))
        {
            return true;
        }

        // Одна опечатка в начале («воспря» ≈ «воспоя…»), только для достаточно длинных токенов
        if (token.Length >= 4 && word.Length >= 4)
        {
            var prefixLen = Math.Min(token.Length, word.Length);
            if (EditDistance(token.AsSpan(0, prefixLen), word.AsSpan(0, prefixLen)) <= 1)
            {
                return true;
            }
        }

        return false;
    }

    private static int EditDistance(ReadOnlySpan<char> a, ReadOnlySpan<char> b)
    {
        if (a.Length == 0)
        {
            return b.Length;
        }

        if (b.Length == 0)
        {
            return a.Length;
        }

        var prev = new int[b.Length + 1];
        var curr = new int[b.Length + 1];
        for (var j = 0; j <= b.Length; j++)
        {
            prev[j] = j;
        }

        for (var i = 1; i <= a.Length; i++)
        {
            curr[0] = i;
            for (var j = 1; j <= b.Length; j++)
            {
                var cost = a[i - 1] == b[j - 1] ? 0 : 1;
                curr[j] = Math.Min(
                    Math.Min(curr[j - 1] + 1, prev[j] + 1),
                    prev[j - 1] + cost);
            }

            (prev, curr) = (curr, prev);
        }

        return prev[b.Length];
    }

    public async Task<Song?> GetSongAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var song = await _dbContext.Songs
            .Include(song => song.Sections)
            .Include(song => song.Attachments)
            .Include(song => song.SongTags)
                .ThenInclude(st => st.Tag)
            .Include(song => song.Collection)
            .AsNoTracking()
            .AsSplitQuery()
            .FirstOrDefaultAsync(song => song.Id == id, cancellationToken);

        if (song is null)
        {
            return null;
        }

        song.Sections = song.Sections
            .OrderBy(section => section.Order)
            .ToList();

        return song;
    }

    public async Task<Song> UpsertSongAsync(Song song, CancellationToken cancellationToken = default)
    {
        if (song.Id == Guid.Empty)
        {
            song.Id = Guid.NewGuid();
        }

        song.UpdatedAt = DateTime.UtcNow;
        // Detached-граф из GetSongs*: не тащить Collection/навигации в трекер
        song.Collection = null;

        var exists = await _dbContext.Songs
            .AsNoTracking()
            .AnyAsync(s => s.Id == song.Id, cancellationToken);

        if (exists)
        {
            // Загружаем существующую сущность для безопасного обновления
            var existing = await _dbContext.Songs
                .Include(s => s.Sections)
                .Include(s => s.SongTags)
                .FirstOrDefaultAsync(s => s.Id == song.Id, cancellationToken);

            if (existing is not null)
            {
                // Обновляем свойства основной сущности
                existing.Title = song.Title;
                existing.Number = song.Number;
                existing.Subtitle = song.Subtitle;
                existing.Language = song.Language;
                existing.DefaultKey = song.DefaultKey;
                existing.Tempo = song.Tempo;
                existing.IsFavorite = song.IsFavorite;
                existing.IsPublished = song.IsPublished;
                existing.CollectionId = song.CollectionId;
                existing.UpdatedAt = song.UpdatedAt;

                // Обновляем секции: удаляем все старые и добавляем новые
                // Это избегает проблем с owned entity Timing
                if (existing.Sections?.Count > 0)
                {
                    _dbContext.SongSections.RemoveRange(existing.Sections);
                }

                // Сохраняем изменения перед добавлением новых секций
                await _dbContext.SaveChangesAsync(cancellationToken);

                // Добавляем новые секции
                if (song.Sections?.Count > 0)
                {
                    foreach (var incomingSection in song.Sections)
                    {
                        if (incomingSection.Id == Guid.Empty)
                        {
                            incomingSection.Id = Guid.NewGuid();
                        }
                        // Иначе EF пытается затрекать и detached Song с тем же Id
                        incomingSection.Song = null!;
                        incomingSection.SongId = existing.Id;
                        if (incomingSection.Timing == null)
                        {
                            incomingSection.Timing = SectionTiming.Empty;
                        }
                        else
                        {
                            incomingSection.Timing = new SectionTiming(
                                incomingSection.Timing.DurationSeconds,
                                incomingSection.Timing.Bpm,
                                incomingSection.Timing.StartMeasure);
                        }
                    }
                    await _dbContext.SongSections.AddRangeAsync(song.Sections, cancellationToken);
                }

                // Обновляем теги только если вызывающий передал коллекцию (null = не трогать)
                if (song.SongTags is not null)
                {
                    if (existing.SongTags?.Count > 0)
                    {
                        _dbContext.SongTags.RemoveRange(existing.SongTags);
                    }

                    if (song.SongTags.Count > 0)
                    {
                        foreach (var tag in song.SongTags)
                        {
                            tag.Song = null!;
                            tag.Tag = null!;
                            tag.SongId = existing.Id;
                        }
                        await _dbContext.SongTags.AddRangeAsync(song.SongTags, cancellationToken);
                    }
                }
            }
        }
        else
        {
            song.CreatedAt = DateTime.UtcNow;
            
            // Автоматически присваиваем номер, если он не указан
            // Очищаем отслеживание перед запросом, чтобы избежать конфликтов
            if (song.Number == null)
            {
                // Очищаем отслеживание перед запросом номеров
                _dbContext.ChangeTracker.Clear();
                
                var existingNumbers = await _dbContext.Songs
                    .AsNoTracking()
                    .Where(s => s.Number != null && s.CollectionId == song.CollectionId)
                    .Select(s => s.Number!.Value)
                    .ToListAsync(cancellationToken);
                
                var usedNumbers = new HashSet<int>(existingNumbers);
                var nextNumber = 1;
                while (usedNumbers.Contains(nextNumber))
                {
                    nextNumber++;
                }
                
                song.Number = nextNumber;
            }
            
            // Убеждаемся, что у всех секций есть ID и отдельный Timing (OwnsOne)
            if (song.Sections?.Count > 0)
            {
                foreach (var section in song.Sections)
                {
                    if (section.Id == Guid.Empty)
                    {
                        section.Id = Guid.NewGuid();
                    }
                    section.Song = null!;
                    section.SongId = song.Id;
                    // Нельзя шарить один экземпляр Timing между секциями — EF меняет SongSectionId
                    section.Timing = section.Timing is null
                        ? SectionTiming.Empty
                        : new SectionTiming(
                            section.Timing.DurationSeconds,
                            section.Timing.Bpm,
                            section.Timing.StartMeasure);
                }
            }

                if (song.SongTags?.Count > 0)
                {
                    foreach (var tag in song.SongTags)
                    {
                        tag.Song = null!;
                        tag.Tag = null!;
                        tag.SongId = song.Id;
                    }
                }
            
            await _dbContext.Songs.AddAsync(song, cancellationToken);
        }

        try
        {
            await _dbContext.SaveChangesAsync(cancellationToken);
        }
        catch
        {
            _dbContext.ChangeTracker.Clear();
            throw;
        }

        // Массовый импорт / повторные Upsert: не копить owned SectionTiming в трекере
        _dbContext.ChangeTracker.Clear();

        return song;
    }

    public async Task RemoveSongAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var song = await _dbContext.Songs
            .Include(s => s.Sections)
            .Include(s => s.SongTags)
            .Include(s => s.Attachments)
            .Include(s => s.Performances)
            .FirstOrDefaultAsync(s => s.Id == id, cancellationToken);

        if (song is null)
        {
            return;
        }

        // Удаляем PlaylistEntry, которые ссылаются на эту песню
        var playlistEntries = await _dbContext.PlaylistEntries
            .Where(e => e.SongId == id)
            .ToListAsync(cancellationToken);
        if (playlistEntries.Count > 0)
        {
            _dbContext.PlaylistEntries.RemoveRange(playlistEntries);
        }

        // Удаляем связанные записи
        if (song.Sections?.Count > 0)
        {
            _dbContext.SongSections.RemoveRange(song.Sections);
        }

        if (song.SongTags?.Count > 0)
        {
            _dbContext.SongTags.RemoveRange(song.SongTags);
        }

        if (song.Attachments?.Count > 0)
        {
            _dbContext.Attachments.RemoveRange(song.Attachments);
        }

        if (song.Performances?.Count > 0)
        {
            _dbContext.PerformanceHistory.RemoveRange(song.Performances);
        }

        _dbContext.Songs.Remove(song);
        await _dbContext.SaveChangesAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<Playlist>> GetPlaylistsAsync(CancellationToken cancellationToken = default)
    {
        var playlists = await _dbContext.Playlists
            .Include(p => p.Entries)
                .ThenInclude(e => e.Song)
                    .ThenInclude(s => s.Sections)
            .Include(p => p.Performances)
            .Include(p => p.ThemePreset)
            .OrderByDescending(p => p.ScheduledAt)
            .ThenByDescending(p => p.Name)
            .AsNoTracking()
            .ToListAsync(cancellationToken);

        foreach (var playlist in playlists)
        {
            SortPlaylistEntriesInPlace(playlist);
        }

        return playlists;
    }

    public async Task<Playlist?> GetPlaylistAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var playlist = await _dbContext.Playlists
            .Include(p => p.Entries)
                .ThenInclude(e => e.Song)
                    .ThenInclude(s => s.Sections)
            .Include(p => p.Performances)
            .Include(p => p.ThemePreset)
            .AsNoTracking()
            .AsSplitQuery()
            .FirstOrDefaultAsync(p => p.Id == id, cancellationToken);

        if (playlist is null)
        {
            return null;
        }

        SortPlaylistEntriesInPlace(playlist);
        return playlist;
    }

    public async Task<Playlist> UpsertPlaylistAsync(Playlist playlist, CancellationToken cancellationToken = default)
    {
        var sanitized = PreparePlaylistForPersistence(playlist);

        var existing = await _dbContext.Playlists
            .Include(p => p.Entries)
            .FirstOrDefaultAsync(p => p.Id == sanitized.Id, cancellationToken);

        if (existing is null)
        {
            await _dbContext.Playlists.AddAsync(sanitized, cancellationToken);
        }
        else
        {
            _dbContext.Entry(existing).CurrentValues.SetValues(sanitized);

            var incomingEntries = sanitized.Entries.ToDictionary(entry => entry.Id);
            var existingEntries = existing.Entries.ToDictionary(entry => entry.Id);

            foreach (var existingEntry in existingEntries.Values)
            {
                if (!incomingEntries.ContainsKey(existingEntry.Id))
                {
                    _dbContext.PlaylistEntries.Remove(existingEntry);
                }
            }

            foreach (var incomingEntry in sanitized.Entries)
            {
                if (existingEntries.TryGetValue(incomingEntry.Id, out var target))
                {
                    _dbContext.Entry(target).CurrentValues.SetValues(incomingEntry);
                }
                else
                {
                    existing.Entries.Add(incomingEntry);
                }
            }
        }

        await _dbContext.SaveChangesAsync(cancellationToken);

        return await GetPlaylistAsync(sanitized.Id, cancellationToken) ?? sanitized;
    }

    public async Task RemovePlaylistAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var entity = await _dbContext.Playlists
            .Include(p => p.Entries)
            .FirstOrDefaultAsync(p => p.Id == id, cancellationToken);

        if (entity is null)
        {
            return;
        }

        if (entity.Entries?.Count > 0)
        {
            _dbContext.PlaylistEntries.RemoveRange(entity.Entries);
        }

        _dbContext.Playlists.Remove(entity);
        await _dbContext.SaveChangesAsync(cancellationToken);
        _dbContext.ChangeTracker.Clear();
    }

    public async Task<IReadOnlyList<ThemePreset>> GetThemePresetsAsync(CancellationToken cancellationToken = default)
    {
        var presets = await _dbContext.ThemePresets
            .AsNoTracking()
            .Include(p => p.Wallpapers)
            .OrderBy(p => p.Name)
            .ToListAsync(cancellationToken);

        return presets;
    }

    public async Task<ThemePreset?> GetThemePresetAsync(Guid id, CancellationToken cancellationToken = default)
    {
        return await _dbContext.ThemePresets
            .AsNoTracking()
            .Include(p => p.Wallpapers)
            .FirstOrDefaultAsync(p => p.Id == id, cancellationToken);
    }

    public async Task<ThemePreset> UpsertThemePresetAsync(ThemePreset preset, CancellationToken cancellationToken = default)
    {
        var sanitized = PrepareThemePresetForPersistence(preset);

        var existing = await _dbContext.ThemePresets
            .FirstOrDefaultAsync(p => p.Id == sanitized.Id, cancellationToken);

        if (existing is null)
        {
            await _dbContext.ThemePresets.AddAsync(sanitized, cancellationToken);
        }
        else
        {
            existing.Name = sanitized.Name;
            existing.FontFamily = sanitized.FontFamily;
            existing.IsBold = sanitized.IsBold;
            existing.TextAlignment = sanitized.TextAlignment;
            existing.SectionTransitionMode = sanitized.SectionTransitionMode;
            existing.SectionTransitionDurationMs = sanitized.SectionTransitionDurationMs;
            existing.Colors = sanitized.Colors;
            existing.BackgroundMediaPath = sanitized.BackgroundMediaPath;
            existing.LoopBackgroundMedia = sanitized.LoopBackgroundMedia;
            existing.UseSeparateBackgrounds = sanitized.UseSeparateBackgrounds;
            existing.BackgroundPickMode = sanitized.BackgroundPickMode;
            existing.SelectedSharedWallpaperId = sanitized.SelectedSharedWallpaperId;
            existing.SelectedSongWallpaperId = sanitized.SelectedSongWallpaperId;
            existing.SelectedBibleWallpaperId = sanitized.SelectedBibleWallpaperId;
            existing.TextOutlineEnabled = sanitized.TextOutlineEnabled;
            existing.TextOutlineThickness = sanitized.TextOutlineThickness;
            existing.TextOutlineColor = sanitized.TextOutlineColor;
            existing.TextOutlineOpacity = sanitized.TextOutlineOpacity;
        }

        await _dbContext.SaveChangesAsync(cancellationToken);

        return await GetThemePresetAsync(sanitized.Id, cancellationToken) ?? sanitized;
    }

    public async Task RemoveThemePresetAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var preset = await _dbContext.ThemePresets
            .Include(p => p.Wallpapers)
            .FirstOrDefaultAsync(p => p.Id == id, cancellationToken);

        if (preset is null)
        {
            return;
        }

        _dbContext.ThemePresets.Remove(preset);
        await _dbContext.SaveChangesAsync(cancellationToken);
    }

    public async Task<ThemeWallpaper> AddThemeWallpaperAsync(
        Guid themePresetId,
        string filePath,
        string displayName,
        ThemeWallpaperPool pool,
        CancellationToken cancellationToken = default)
    {
        var presetExists = await _dbContext.ThemePresets
            .AnyAsync(p => p.Id == themePresetId, cancellationToken);
        if (!presetExists)
        {
            throw new InvalidOperationException("Стиль не найден.");
        }

        var maxOrder = await _dbContext.ThemeWallpapers
            .Where(w => w.ThemePresetId == themePresetId && w.Pool == pool)
            .Select(w => (int?)w.SortOrder)
            .MaxAsync(cancellationToken) ?? -1;

        var wallpaper = new ThemeWallpaper
        {
            Id = Guid.NewGuid(),
            ThemePresetId = themePresetId,
            FilePath = filePath.Trim(),
            DisplayName = string.IsNullOrWhiteSpace(displayName) ? Path.GetFileName(filePath) : displayName.Trim(),
            Pool = pool,
            SortOrder = maxOrder + 1
        };

        await _dbContext.ThemeWallpapers.AddAsync(wallpaper, cancellationToken);

        var preset = await _dbContext.ThemePresets
            .FirstAsync(p => p.Id == themePresetId, cancellationToken);

        if (ThemeBackgroundResolver.GetSelectedId(preset, pool) is null)
        {
            ThemeBackgroundResolver.SetSelectedId(preset, pool, wallpaper.Id);
        }

        // Держим legacy-поле в синхроне для Shared
        if (pool == ThemeWallpaperPool.Shared
            && string.IsNullOrWhiteSpace(preset.BackgroundMediaPath))
        {
            preset.BackgroundMediaPath = wallpaper.FilePath;
        }

        await _dbContext.SaveChangesAsync(cancellationToken);
        return wallpaper;
    }

    public async Task UpdateThemeWallpaperDisplayNameAsync(
        Guid wallpaperId,
        string displayName,
        CancellationToken cancellationToken = default)
    {
        var wallpaper = await _dbContext.ThemeWallpapers
            .FirstOrDefaultAsync(w => w.Id == wallpaperId, cancellationToken);
        if (wallpaper is null)
        {
            return;
        }

        var name = string.IsNullOrWhiteSpace(displayName)
            ? Path.GetFileNameWithoutExtension(wallpaper.FilePath)
            : displayName.Trim();
        if (name.Length > 256)
        {
            name = name[..256];
        }

        if (string.Equals(wallpaper.DisplayName, name, StringComparison.Ordinal))
        {
            return;
        }

        wallpaper.DisplayName = name;
        await _dbContext.SaveChangesAsync(cancellationToken);
    }

    public async Task RemoveThemeWallpaperAsync(Guid wallpaperId, CancellationToken cancellationToken = default)
    {
        var wallpaper = await _dbContext.ThemeWallpapers
            .FirstOrDefaultAsync(w => w.Id == wallpaperId, cancellationToken);
        if (wallpaper is null)
        {
            return;
        }

        var preset = await _dbContext.ThemePresets
            .FirstOrDefaultAsync(p => p.Id == wallpaper.ThemePresetId, cancellationToken);

        _dbContext.ThemeWallpapers.Remove(wallpaper);

        if (preset is not null)
        {
            if (preset.SelectedSharedWallpaperId == wallpaperId)
            {
                preset.SelectedSharedWallpaperId = null;
            }

            if (preset.SelectedSongWallpaperId == wallpaperId)
            {
                preset.SelectedSongWallpaperId = null;
            }

            if (preset.SelectedBibleWallpaperId == wallpaperId)
            {
                preset.SelectedBibleWallpaperId = null;
            }

            if (string.Equals(preset.BackgroundMediaPath, wallpaper.FilePath, StringComparison.OrdinalIgnoreCase))
            {
                var replacement = await _dbContext.ThemeWallpapers
                    .Where(w => w.ThemePresetId == preset.Id
                                && w.Pool == ThemeWallpaperPool.Shared
                                && w.Id != wallpaperId)
                    .OrderBy(w => w.SortOrder)
                    .FirstOrDefaultAsync(cancellationToken);
                preset.BackgroundMediaPath = replacement?.FilePath;
            }
        }

        await _dbContext.SaveChangesAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<SongCollection>> GetSongCollectionsAsync(CancellationToken cancellationToken = default)
    {
        return await _dbContext.SongCollections
            .AsNoTracking()
            .OrderBy(c => c.SortOrder)
            .ThenBy(c => c.Name)
            .ToListAsync(cancellationToken);
    }

    public async Task<SongCollection> UpsertSongCollectionAsync(SongCollection collection, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(collection);
        collection.Name = collection.Name.Trim();
        collection.UpdatedAt = DateTime.UtcNow;

        if (collection.Id == Guid.Empty)
        {
            collection.Id = Guid.NewGuid();
        }

        var existing = await _dbContext.SongCollections
            .FirstOrDefaultAsync(c => c.Id == collection.Id, cancellationToken);

        if (existing is null)
        {
            collection.CreatedAt = DateTime.UtcNow;
            _dbContext.SongCollections.Add(collection);
        }
        else
        {
            existing.Name = collection.Name;
            existing.Description = collection.Description;
            existing.SortOrder = collection.SortOrder;
            existing.UpdatedAt = collection.UpdatedAt;
            collection = existing;
        }

        await _dbContext.SaveChangesAsync(cancellationToken);
        return collection;
    }

    public async Task RemoveSongCollectionAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var collection = await _dbContext.SongCollections
            .FirstOrDefaultAsync(c => c.Id == id, cancellationToken);
        if (collection is null)
        {
            return;
        }

        var songIds = await _dbContext.Songs
            .Where(s => s.CollectionId == id)
            .Select(s => s.Id)
            .ToListAsync(cancellationToken);

        if (songIds.Count > 0)
        {
            var playlistEntries = await _dbContext.PlaylistEntries
                .Where(e => songIds.Contains(e.SongId))
                .ToListAsync(cancellationToken);
            if (playlistEntries.Count > 0)
            {
                _dbContext.PlaylistEntries.RemoveRange(playlistEntries);
            }

            var sections = await _dbContext.SongSections
                .Where(s => songIds.Contains(s.SongId))
                .ToListAsync(cancellationToken);
            if (sections.Count > 0)
            {
                _dbContext.SongSections.RemoveRange(sections);
            }

            var tags = await _dbContext.SongTags
                .Where(t => songIds.Contains(t.SongId))
                .ToListAsync(cancellationToken);
            if (tags.Count > 0)
            {
                _dbContext.SongTags.RemoveRange(tags);
            }

            var attachments = await _dbContext.Attachments
                .Where(a => songIds.Contains(a.SongId))
                .ToListAsync(cancellationToken);
            if (attachments.Count > 0)
            {
                _dbContext.Attachments.RemoveRange(attachments);
            }

            var performances = await _dbContext.PerformanceHistory
                .Where(p => songIds.Contains(p.SongId))
                .ToListAsync(cancellationToken);
            if (performances.Count > 0)
            {
                _dbContext.PerformanceHistory.RemoveRange(performances);
            }

            var songs = await _dbContext.Songs
                .Where(s => songIds.Contains(s.Id))
                .ToListAsync(cancellationToken);
            _dbContext.Songs.RemoveRange(songs);
        }

        _dbContext.SongCollections.Remove(collection);
        await _dbContext.SaveChangesAsync(cancellationToken);
        _dbContext.ChangeTracker.Clear();
    }

    public async Task<IReadOnlyList<Song>> GetSongsByCollectionAsync(Guid? collectionId, CancellationToken cancellationToken = default)
    {
        var query = _dbContext.Songs
            .Include(song => song.Sections)
            .Include(song => song.SongTags)
                .ThenInclude(st => st.Tag)
            .Include(song => song.Collection)
            .AsNoTracking()
            .AsSplitQuery()
            .AsQueryable();

        query = collectionId is null
            ? query.Where(s => s.CollectionId == null)
            : query.Where(s => s.CollectionId == collectionId);

        var songs = await query
            .OrderBy(s => s.Number ?? int.MaxValue)
            .ThenBy(s => s.Title)
            .ToListAsync(cancellationToken);

        return songs
            .Select(song =>
            {
                song.Sections = song.Sections.OrderBy(section => section.Order).ToList();
                return song;
            })
            .ToList();
    }

    public async Task RecordSongPlayAsync(Guid songId, CancellationToken cancellationToken = default)
    {
        var exists = await _dbContext.Songs
            .AsNoTracking()
            .AnyAsync(s => s.Id == songId, cancellationToken);
        if (!exists)
        {
            return;
        }

        _dbContext.PerformanceHistory.Add(new PerformanceHistory
        {
            Id = Guid.NewGuid(),
            SongId = songId,
            PlayedAt = DateTime.UtcNow
        });
        await _dbContext.SaveChangesAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<TopSongStat>> GetTopSongsAsync(
        int take = 30,
        Guid? collectionId = null,
        CancellationToken cancellationToken = default)
    {
        take = Math.Clamp(take, 1, 100);

        var songsQuery = _dbContext.Songs
            .AsNoTracking()
            .Where(s => !s.IsArchived)
            .AsQueryable();

        if (collectionId is Guid id)
        {
            songsQuery = songsQuery.Where(s => s.CollectionId == id);
        }

        var playCounts = await _dbContext.PerformanceHistory
            .AsNoTracking()
            .GroupBy(p => p.SongId)
            .Select(g => new
            {
                SongId = g.Key,
                PlayCount = g.Count(),
                LastPlayedAt = g.Max(x => x.PlayedAt)
            })
            .ToListAsync(cancellationToken);

        var playMap = playCounts.ToDictionary(x => x.SongId, x => x);

        var songs = await songsQuery
            .Include(s => s.Collection)
            .ToListAsync(cancellationToken);

        var ranked = songs
            .Select(s =>
            {
                playMap.TryGetValue(s.Id, out var stats);
                return new TopSongStat
                {
                    SongId = s.Id,
                    Title = s.Title,
                    Number = s.Number,
                    CollectionId = s.CollectionId,
                    CollectionName = s.Collection?.Name,
                    PlayCount = stats?.PlayCount ?? 0,
                    LastPlayedAt = stats?.LastPlayedAt
                };
            })
            .Where(s => s.PlayCount > 0)
            .OrderByDescending(s => s.PlayCount)
            .ThenByDescending(s => s.LastPlayedAt ?? DateTime.MinValue)
            .ThenBy(s => s.Title, StringComparer.CurrentCultureIgnoreCase)
            .Take(take)
            .ToList();

        return ranked;
    }

    private static void SortPlaylistEntriesInPlace(Playlist playlist)
    {
        playlist.Entries = playlist.Entries
            .OrderBy(entry => entry.Order)
            .ToList();
    }

    private static Playlist PreparePlaylistForPersistence(Playlist playlist)
    {
        var playlistId = playlist.Id == Guid.Empty ? Guid.NewGuid() : playlist.Id;

        var name = string.IsNullOrWhiteSpace(playlist.Name)
            ? "Без названия"
            : playlist.Name.Trim();

        var sanitized = new Playlist
        {
            Id = playlistId,
            Name = name,
            EventType = playlist.EventType,
            ScheduledAt = playlist.ScheduledAt,
            Location = string.IsNullOrWhiteSpace(playlist.Location) ? null : playlist.Location.Trim(),
            ThemePresetId = playlist.ThemePresetId
        };

        var orderedEntries = playlist.Entries?
            .OrderBy(e => e.Order)
            .Select((entry, index) => (entry, index))
            ?? Enumerable.Empty<(PlaylistEntry entry, int index)>();

        foreach (var (entry, index) in orderedEntries)
        {
            var entryId = entry.Id == Guid.Empty ? Guid.NewGuid() : entry.Id;

            sanitized.Entries.Add(new PlaylistEntry
            {
                Id = entryId,
                PlaylistId = playlistId,
                SongId = entry.SongId,
                AttachmentId = entry.AttachmentId,
                TransposeSteps = entry.TransposeSteps,
                TempoOverride = entry.TempoOverride,
                Cues = entry.Cues,
                Order = index
            });
        }

        return sanitized;
    }

    private static ThemePreset PrepareThemePresetForPersistence(ThemePreset preset)
    {
        var presetId = preset.Id == Guid.Empty ? Guid.NewGuid() : preset.Id;

        var name = string.IsNullOrWhiteSpace(preset.Name)
            ? "Новый стиль"
            : preset.Name.Trim();

        var backgroundMediaPath = string.IsNullOrWhiteSpace(preset.BackgroundMediaPath)
            ? null
            : preset.BackgroundMediaPath.Trim();

        var colors = preset.Colors ?? ThemeColors.Default;

        return new ThemePreset
        {
            Id = presetId,
            Name = name,
            FontFamily = string.IsNullOrWhiteSpace(preset.FontFamily) ? null : preset.FontFamily.Trim(),
            IsBold = preset.IsBold,
            TextAlignment = string.IsNullOrWhiteSpace(preset.TextAlignment) ? "Center" : preset.TextAlignment,
            SectionTransitionMode = preset.SectionTransitionMode,
            SectionTransitionDurationMs = NormalizeTransitionDuration(preset.SectionTransitionDurationMs),
            Colors = new ThemeColors(
                NormalizeColor(colors.Primary, ThemeColors.Default.Primary),
                NormalizeColor(colors.Background, ThemeColors.Default.Background)),
            BackgroundMediaPath = backgroundMediaPath,
            LoopBackgroundMedia = preset.LoopBackgroundMedia,
            UseSeparateBackgrounds = preset.UseSeparateBackgrounds,
            BackgroundPickMode = preset.BackgroundPickMode,
            SelectedSharedWallpaperId = preset.SelectedSharedWallpaperId,
            SelectedSongWallpaperId = preset.SelectedSongWallpaperId,
            SelectedBibleWallpaperId = preset.SelectedBibleWallpaperId,
            TextOutlineEnabled = preset.TextOutlineEnabled,
            TextOutlineThickness = Math.Clamp(preset.TextOutlineThickness, 0, 40),
            TextOutlineColor = NormalizeColor(
                string.IsNullOrWhiteSpace(preset.TextOutlineColor) ? "#000000" : preset.TextOutlineColor,
                "#000000"),
            TextOutlineOpacity = Math.Clamp(preset.TextOutlineOpacity, 0, 1)
        };
    }

    private static int NormalizeTransitionDuration(int ms) =>
        Math.Clamp(ms <= 0 ? 750 : ms, 150, 3000);

    private static string NormalizeColor(string value, string fallback)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return fallback;
        }

        var trimmed = value.Trim();
        trimmed = trimmed.StartsWith('#') ? trimmed : $"#{trimmed}";
        return trimmed.ToUpperInvariant();
    }
}

