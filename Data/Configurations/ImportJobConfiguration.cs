using ChyguiSlide.Data.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace ChyguiSlide.Data.Configurations;

public class ImportJobConfiguration : IEntityTypeConfiguration<ImportJob>
{
    public void Configure(EntityTypeBuilder<ImportJob> builder)
    {
        builder.ToTable("ImportJobs");

        builder.HasKey(x => x.Id);

        builder.Property(x => x.SourceType)
            .HasConversion<string>()
            .HasMaxLength(32);

        builder.Property(x => x.Status)
            .HasConversion<string>()
            .HasMaxLength(32);

        builder.Property(x => x.SourceLocation)
            .IsRequired()
            .HasMaxLength(1024);

        builder.Property(x => x.Progress)
            .HasDefaultValue(0d);

        builder.Property(x => x.CreatedAt)
            .HasDefaultValueSql("CURRENT_TIMESTAMP");

        builder.Property(x => x.UpdatedAt)
            .HasDefaultValueSql("CURRENT_TIMESTAMP");
    }
}

