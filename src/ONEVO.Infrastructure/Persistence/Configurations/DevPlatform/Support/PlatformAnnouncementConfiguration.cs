using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using ONEVO.Domain.Features.DevPlatform.Support.Entities;

namespace ONEVO.Infrastructure.Persistence.Configurations.DevPlatform.Support;

public sealed class PlatformAnnouncementConfiguration : IEntityTypeConfiguration<PlatformAnnouncement>
{
    public void Configure(EntityTypeBuilder<PlatformAnnouncement> builder)
    {
        builder.ToTable("platform_announcements");
        builder.HasKey(a => a.Id);

        builder.Property(a => a.Title).HasColumnName("title").HasMaxLength(200).IsRequired();
        builder.Property(a => a.Body).HasColumnName("body").HasMaxLength(4000).IsRequired();
        builder.Property(a => a.Severity).HasColumnName("severity").HasMaxLength(20).IsRequired();
        builder.Property(a => a.Audience).HasColumnName("audience").HasMaxLength(20).IsRequired();
        builder.Property(a => a.IsPublished).HasColumnName("is_published").IsRequired();
        builder.Property(a => a.PublishedAt).HasColumnName("published_at");
        builder.Property(a => a.CreatedAt).HasColumnName("created_at").IsRequired();
        builder.Property(a => a.UpdatedAt).HasColumnName("updated_at");

        builder.HasIndex(a => a.IsPublished);
        builder.HasIndex(a => a.Severity);
        builder.HasIndex(a => a.CreatedAt);
    }
}
