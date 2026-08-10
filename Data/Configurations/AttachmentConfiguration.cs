using ChyguiSlide.Data.Entities;
using ChyguiSlide.Data.ValueObjects;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace ChyguiSlide.Data.Configurations;

public class AttachmentConfiguration : IEntityTypeConfiguration<Attachment>
{
    public void Configure(EntityTypeBuilder<Attachment> builder)
    {
        builder.ToTable("Attachments");

        builder.HasKey(x => x.Id);

        builder.Property(x => x.Kind)
            .HasConversion<string>()
            .HasMaxLength(32);

        builder.Property(x => x.FilePath)
            .IsRequired()
            .HasMaxLength(512);

        builder.Property(x => x.MimeType)
            .HasMaxLength(128);

        builder.Property(x => x.Version)
            .HasMaxLength(64);

        builder.Property(x => x.CloudUri)
            .HasMaxLength(1024);

        builder.HasIndex(x => x.SongId)
            .HasDatabaseName("IX_Attachments_SongId");

        builder.OwnsOne(
            x => x.CloudLocation,
            (OwnedNavigationBuilder<Attachment, CloudLocation> cloud) =>
        {
            cloud.Property(c => c.Provider)
                .HasMaxLength(64)
                .HasColumnName("CloudProvider");

            cloud.Property(c => c.RemotePath)
                .HasMaxLength(512)
                .HasColumnName("CloudRemotePath");

            cloud.Property(c => c.SyncedAtUtc)
                .HasColumnName("CloudSyncedAtUtc");
        });

        builder.HasOne(x => x.Song)
            .WithMany(x => x.Attachments)
            .HasForeignKey(x => x.SongId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne(x => x.ImportJob)
            .WithMany(x => x.Attachments)
            .HasForeignKey(x => x.ImportJobId)
            .OnDelete(DeleteBehavior.SetNull);
    }
}

