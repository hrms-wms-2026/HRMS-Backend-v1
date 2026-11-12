using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using ONEVO.Domain.Features.DevPlatform.PlatformAccess.Entities;
using ONEVO.Domain.Features.DevPlatform.Support.Entities;

namespace ONEVO.Infrastructure.Persistence.Configurations.DevPlatform.Support;

public sealed class PlatformAnnouncementConfiguration : IEntityTypeConfiguration<PlatformAnnouncement>
{
    public void Configure(EntityTypeBuilder<PlatformAnnouncement> builder)
    {
        builder.ToTable("platform_announcements");
        builder.HasKey(x => x.Id);

        builder.Property(x => x.Title).HasMaxLength(200).IsRequired();
        builder.Property(x => x.Body).HasMaxLength(5000).IsRequired();
        builder.Property(x => x.Severity).HasMaxLength(20).IsRequired();
        builder.Property(x => x.Audience).HasMaxLength(30).IsRequired();
        builder.Property(x => x.IsPublished).IsRequired();
        builder.Property(x => x.CreatedAt).IsRequired();

        builder.Ignore(x => x.IsVisibleToTenants);
        builder.Ignore(x => x.IsVisibleToPlatformAdmins);

        builder.HasIndex(x => new { x.IsPublished, x.Audience, x.PublishedAt })
            .HasDatabaseName("ix_platform_announcements_published_audience");
        builder.HasIndex(x => x.CreatedAt)
            .HasDatabaseName("ix_platform_announcements_created_at");

        // Restrict, not SetNull: two SET NULL paths from platform_users into one table are
        // "multiple cascade paths" on SQL Server. Platform users are deactivated, never deleted.
        builder.HasOne<PlatformUser>()
            .WithMany()
            .HasForeignKey(x => x.CreatedByPlatformUserId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<PlatformUser>()
            .WithMany()
            .HasForeignKey(x => x.PublishedByPlatformUserId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
