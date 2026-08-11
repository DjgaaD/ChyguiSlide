using ChyguiSlide.Data.Entities;
using ChyguiSlide.Data.Enums;
using ChyguiSlide.Services.Models;

namespace ChyguiSlide.Services.Abstractions;

public interface ICatalogService
{
    Task<IReadOnlyList<Song>> GetSongsAsync(CancellationToken cancellationToken = default);
    Task<IReadOnlyList<Song>> SearchSongsAsync(string query, CancellationToken cancellationToken = default);
    Task<Song?> GetSongAsync(Guid id, CancellationToken cancellationToken = default);
    Task<Song> UpsertSongAsync(Song song, CancellationToken cancellationToken = default);
    Task RemoveSongAsync(Guid id, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<Playlist>> GetPlaylistsAsync(CancellationToken cancellationToken = default);
    Task<Playlist?> GetPlaylistAsync(Guid id, CancellationToken cancellationToken = default);
    Task<Playlist> UpsertPlaylistAsync(Playlist playlist, CancellationToken cancellationToken = default);
    Task RemovePlaylistAsync(Guid id, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<ThemePreset>> GetThemePresetsAsync(CancellationToken cancellationToken = default);
    Task<ThemePreset?> GetThemePresetAsync(Guid id, CancellationToken cancellationToken = default);
    Task<ThemePreset> UpsertThemePresetAsync(ThemePreset preset, CancellationToken cancellationToken = default);
    Task RemoveThemePresetAsync(Guid id, CancellationToken cancellationToken = default);
    Task<ThemeWallpaper> AddThemeWallpaperAsync(
        Guid themePresetId,
        string filePath,
        string displayName,
        ThemeWallpaperPool pool,
        CancellationToken cancellationToken = default);
    Task RemoveThemeWallpaperAsync(Guid wallpaperId, CancellationToken cancellationToken = default);
    Task UpdateThemeWallpaperDisplayNameAsync(
        Guid wallpaperId,
        string displayName,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<SongCollection>> GetSongCollectionsAsync(CancellationToken cancellationToken = default);
    Task<SongCollection> UpsertSongCollectionAsync(SongCollection collection, CancellationToken cancellationToken = default);
    Task RemoveSongCollectionAsync(Guid id, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<Song>> GetSongsByCollectionAsync(Guid? collectionId, CancellationToken cancellationToken = default);

    Task RecordSongPlayAsync(Guid songId, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<TopSongStat>> GetTopSongsAsync(int take = 30, Guid? collectionId = null, CancellationToken cancellationToken = default);
}

