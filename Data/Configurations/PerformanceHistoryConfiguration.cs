using ChyguiSlide.Data.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace ChyguiSlide.Data.Configurations;

public class PerformanceHistoryConfiguration : IEntityTypeConfiguration<PerformanceHistory>
{
    public void Configure(EntityTypeBuilder<PerformanceHistory> builder)
    {
        builder.ToTable("PerformanceHistory");

        builder.HasKey(x => x.Id);

        builder.HasIndex(x => x.PlayedAt)
            .HasDatabaseName("IX_PerformanceHistory_PlayedAt");

        builder.Property(x => x.OperatorName)
            .HasMaxLength(128);

        builder.HasOne(x => x.Song)
            .WithMany(x => x.Performances)
            .HasForeignKey(x => x.SongId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne(x => x.Playlist)
            .WithMany(x => x.Performances)
            .HasForeignKey(x => x.PlaylistId)
            .OnDelete(DeleteBehavior.SetNull);

        builder.HasOne(x => x.ThemePreset)
            .WithMany(x => x.Performances)
            .HasForeignKey(x => x.ThemePresetId)
            .OnDelete(DeleteBehavior.SetNull);
    }
}

