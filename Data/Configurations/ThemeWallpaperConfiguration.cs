using ChyguiSlide.Data.Entities;
using ChyguiSlide.Data.Enums;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace ChyguiSlide.Data.Configurations;

public class ThemeWallpaperConfiguration : IEntityTypeConfiguration<ThemeWallpaper>
{
    public void Configure(EntityTypeBuilder<ThemeWallpaper> builder)
    {
        builder.ToTable("ThemeWallpapers");
        builder.HasKey(x => x.Id);

        builder.Property(x => x.FilePath)
            .IsRequired()
            .HasMaxLength(512);

        builder.Property(x => x.DisplayName)
            .IsRequired()
            .HasMaxLength(256);

        builder.Property(x => x.Pool)
            .HasConversion<int>();

        builder.HasIndex(x => new { x.ThemePresetId, x.Pool, x.SortOrder });

        builder.HasOne(x => x.ThemePreset)
            .WithMany(x => x.Wallpapers)
            .HasForeignKey(x => x.ThemePresetId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
