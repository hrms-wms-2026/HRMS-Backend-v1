using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using ONEVO.Domain.Features.WorkManagement.Projects.Entities;
using ONEVO.Domain.Features.WorkManagement.ReleaseCalendar.Entities;
using ONEVO.Domain.Features.WorkManagement.Versions.Entities;

namespace ONEVO.Infrastructure.Persistence.Configurations.WorkManagement;

public class ReleaseCalendarEntryConfiguration : IEntityTypeConfiguration<ReleaseCalendarEntry>
{
    public void Configure(EntityTypeBuilder<ReleaseCalendarEntry> builder)
    {
        builder.ToTable("release_calendar");
        builder.HasKey(r => r.Id);
        builder.Property(r => r.ReminderType).HasMaxLength(30).IsRequired();

        builder.HasIndex(r => new { r.TenantId, r.RecipientUserId, r.ScheduledDate, r.IsActive })
            .HasDatabaseName("ix_release_calendar_tenant_recipient_scheduled_active");
        builder.HasIndex(r => new { r.VersionId, r.RecipientUserId })
            .IsUnique()
            .HasFilter("is_active = true AND reminder_type = 'project_release'")
            .HasDatabaseName("ix_release_calendar_one_active_project_release");

        builder.HasOne<Project>().WithMany().HasForeignKey(r => r.ProjectId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<ProjectVersion>().WithMany().HasForeignKey(r => r.VersionId).OnDelete(DeleteBehavior.Restrict);
    }
}
