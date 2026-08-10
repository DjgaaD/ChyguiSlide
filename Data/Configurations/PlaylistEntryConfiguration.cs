using ChyguiSlide.Data.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace ChyguiSlide.Data.Configurations;

public class PlaylistEntryConfiguration : IEntityTypeConfiguration<PlaylistEntry>
{
    public void Configure(EntityTypeBuilder<PlaylistEntry> builder)
    {
        builder.ToTable("PlaylistEntries");

        builder.HasKey(x => x.Id);

        builder.Property(x => x.Order)
            .IsRequired();

        builder.HasIndex(x => new { x.PlaylistId, x.Order })
            .HasDatabaseName("IX_PlaylistEntries_Order")
            .IsUnique();

        builder.Property(x => x.Cues)
            .HasMaxLength(512);

        builder.HasOne(x => x.Playlist)
            .WithMany(x => x.Entries)
            .HasForeignKey(x => x.PlaylistId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne(x => x.Song)
            .WithMany()
            .HasForeignKey(x => x.SongId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(x => x.Attachment)
            .WithMany()
            .HasForeignKey(x => x.AttachmentId)
            .OnDelete(DeleteBehavior.SetNull);
    }
}

