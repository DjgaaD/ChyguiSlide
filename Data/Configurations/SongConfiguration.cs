using ChyguiSlide.Data.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace ChyguiSlide.Data.Configurations;

public class SongConfiguration : IEntityTypeConfiguration<Song>
{
    public void Configure(EntityTypeBuilder<Song> builder)
    {
        builder.ToTable("Songs");

        builder.HasKey(x => x.Id);

        builder.Property(x => x.Title)
            .IsRequired()
            .HasMaxLength(256);

        builder.Property(x => x.Language)
            .HasMaxLength(32);

        builder.Property(x => x.DefaultKey)
            .HasMaxLength(64);

        builder.Property(x => x.Number);

        // Номер уникален в рамках сборника (или среди песен без сборника)
        builder.HasIndex(x => new { x.CollectionId, x.Number })
            .IsUnique()
            .HasDatabaseName("IX_Songs_Collection_Number");

        builder.Property(x => x.Subtitle)
            .HasMaxLength(256);

        // Не уникальный: в сборниках встречаются одинаковые названия,
        // а также пересечения между редакциями (3055 / 3300).
        builder.HasIndex(x => new { x.Title, x.Language })
            .HasDatabaseName("IX_Songs_Title_Language");

        builder.HasQueryFilter(song => !song.IsArchived);

        builder.Property(x => x.CreatedAt)
            .HasDefaultValueSql("CURRENT_TIMESTAMP");

        builder.Property(x => x.UpdatedAt)
            .HasDefaultValueSql("CURRENT_TIMESTAMP");

        builder.HasMany(song => song.Sections)
            .WithOne(section => section.Song)
            .HasForeignKey(section => section.SongId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}

