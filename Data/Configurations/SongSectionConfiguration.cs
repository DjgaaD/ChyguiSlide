using ChyguiSlide.Data.Entities;
using ChyguiSlide.Data.ValueObjects;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace ChyguiSlide.Data.Configurations;

public class SongSectionConfiguration : IEntityTypeConfiguration<SongSection>
{
    public void Configure(EntityTypeBuilder<SongSection> builder)
    {
        builder.ToTable("SongSections");

        builder.HasKey(x => x.Id);
        builder.HasIndex(x => new { x.SongId, x.Order })
            .HasDatabaseName("IX_SongSections_Order");

        builder.Property(x => x.Heading)
            .HasMaxLength(128);

        builder.Property(x => x.SectionType)
            .HasConversion<string>()
            .HasMaxLength(32);

        builder.OwnsOne(
            x => x.Timing,
            (OwnedNavigationBuilder<SongSection, SectionTiming> timing) =>
        {
            timing.Property(t => t.DurationSeconds)
                .HasColumnName("DurationSeconds");

            timing.Property(t => t.Bpm)
                .HasColumnName("Bpm");

            timing.Property(t => t.StartMeasure)
                .HasColumnName("StartMeasure");
        });
    }
}

