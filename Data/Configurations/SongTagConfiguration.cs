using ChyguiSlide.Data.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace ChyguiSlide.Data.Configurations;

public class SongTagConfiguration : IEntityTypeConfiguration<SongTag>
{
    public void Configure(EntityTypeBuilder<SongTag> builder)
    {
        builder.ToTable("SongTags");
        builder.HasKey(x => new { x.SongId, x.TagId });

        builder.HasOne(x => x.Song)
            .WithMany(x => x.SongTags)
            .HasForeignKey(x => x.SongId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne(x => x.Tag)
            .WithMany(x => x.SongTags)
            .HasForeignKey(x => x.TagId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}

