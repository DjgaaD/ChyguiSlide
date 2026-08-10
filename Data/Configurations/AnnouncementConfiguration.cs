using ChyguiSlide.Data.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace ChyguiSlide.Data.Configurations;

public class AnnouncementConfiguration : IEntityTypeConfiguration<Announcement>
{
    public void Configure(EntityTypeBuilder<Announcement> builder)
    {
        builder.ToTable("Announcements");

        builder.HasKey(x => x.Id);

        builder.Property(x => x.Title)
            .IsRequired()
            .HasMaxLength(256);

        builder.Property(x => x.Content)
            .IsRequired();

        builder.Property(x => x.IsPermanent)
            .IsRequired();

        builder.Property(x => x.IsPinned)
            .IsRequired();

        builder.Property(x => x.SortOrder)
            .IsRequired();

        builder.Property(x => x.CreatedAt)
            .IsRequired();

        builder.Property(x => x.UpdatedAt)
            .IsRequired();

        builder.HasIndex(x => new { x.IsPinned, x.SortOrder, x.UpdatedAt })
            .HasDatabaseName("IX_Announcements_List");
    }
}
