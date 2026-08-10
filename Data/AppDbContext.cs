using ChyguiSlide.Data.Entities;
using Microsoft.EntityFrameworkCore;

namespace ChyguiSlide.Data;

public class AppDbContext(DbContextOptions<AppDbContext> options) : DbContext(options)
{
    public DbSet<Song> Songs => Set<Song>();
    public DbSet<SongSection> SongSections => Set<SongSection>();
    public DbSet<Tag> Tags => Set<Tag>();
    public DbSet<SongTag> SongTags => Set<SongTag>();
    public DbSet<Playlist> Playlists => Set<Playlist>();
    public DbSet<PlaylistEntry> PlaylistEntries => Set<PlaylistEntry>();
    public DbSet<PerformanceHistory> PerformanceHistory => Set<PerformanceHistory>();
    public DbSet<Attachment> Attachments => Set<Attachment>();
    public DbSet<ImportJob> ImportJobs => Set<ImportJob>();
    public DbSet<ThemePreset> ThemePresets => Set<ThemePreset>();
    public DbSet<ThemeWallpaper> ThemeWallpapers => Set<ThemeWallpaper>();
    public DbSet<Announcement> Announcements => Set<Announcement>();
    public DbSet<SongCollection> SongCollections => Set<SongCollection>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(AppDbContext).Assembly);
        base.OnModelCreating(modelBuilder);
    }
}

