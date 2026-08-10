using ChyguiSlide.Data.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace ChyguiSlide.Data.Configurations;

public class SongCollectionConfiguration : IEntityTypeConfiguration<SongCollection>
{
    public void Configure(EntityTypeBuilder<SongCollection> builder)
    {
        builder.ToTable("SongCollections");

        builder.HasKey(x => x.Id);

        builder.Property(x => x.Name)
            .IsRequired()
            .HasMaxLength(256);

        builder.HasIndex(x => x.Name)
            .IsUnique()
            .HasDatabaseName("IX_SongCollections_Name");

        builder.Property(x => x.Description)
            .HasMaxLength(512);

        builder.HasMany(x => x.Songs)
            .WithOne(s => s.Collection)
            .HasForeignKey(s => s.CollectionId)
            .OnDelete(DeleteBehavior.SetNull);
    }
}
