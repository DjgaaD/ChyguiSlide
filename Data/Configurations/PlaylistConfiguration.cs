using ChyguiSlide.Data.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace ChyguiSlide.Data.Configurations;

public class PlaylistConfiguration : IEntityTypeConfiguration<Playlist>
{
    public void Configure(EntityTypeBuilder<Playlist> builder)
    {
        builder.ToTable("Playlists");

        builder.HasKey(x => x.Id);

        builder.Property(x => x.Name)
            .IsRequired()
            .HasMaxLength(256);

        builder.Property(x => x.EventType)
            .HasMaxLength(128);

        builder.Property(x => x.Location)
            .HasMaxLength(256);

        builder.HasOne(x => x.ThemePreset)
            .WithMany(x => x.Playlists)
            .HasForeignKey(x => x.ThemePresetId)
            .OnDelete(DeleteBehavior.SetNull);
    }
}

