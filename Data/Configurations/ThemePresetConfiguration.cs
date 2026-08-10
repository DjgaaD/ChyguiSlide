using ChyguiSlide.Data.Entities;
using ChyguiSlide.Data.Enums;
using ChyguiSlide.Data.ValueObjects;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace ChyguiSlide.Data.Configurations;

public class ThemePresetConfiguration : IEntityTypeConfiguration<ThemePreset>
{
    public void Configure(EntityTypeBuilder<ThemePreset> builder)
    {
        builder.ToTable("ThemePresets");

        builder.HasKey(x => x.Id);

        builder.Property(x => x.Name)
            .IsRequired()
            .HasMaxLength(128);

        builder.Property(x => x.FontFamily)
            .HasMaxLength(128);

        builder.Property(x => x.IsBold);

        builder.Property(x => x.TextAlignment)
            .HasMaxLength(16);

        builder.Property(x => x.SectionTransitionMode)
            .HasConversion<int>();

        builder.Property(x => x.BackgroundMediaPath)
            .HasMaxLength(512);

        builder.Property(x => x.BackgroundPickMode)
            .HasConversion<int>();

        builder.Property(x => x.TextOutlineEnabled);

        builder.Property(x => x.TextOutlineThickness);

        builder.Property(x => x.TextOutlineColor)
            .HasMaxLength(16);

        builder.Property(x => x.TextOutlineOpacity);

        builder.OwnsOne(
            x => x.Colors,
            (OwnedNavigationBuilder<ThemePreset, ThemeColors> colors) =>
        {
            colors.Property(c => c.Primary)
                .HasMaxLength(16)
                .HasColumnName("ColorPrimary");

            colors.Property(c => c.Background)
                .HasMaxLength(16)
                .HasColumnName("ColorBackground");
        });
    }
}

