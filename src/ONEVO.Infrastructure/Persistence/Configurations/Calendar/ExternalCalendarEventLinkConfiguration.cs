using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using ONEVO.Domain.Features.Calendar.Entities;
using ONEVO.Domain.Features.WorkManagement.Meetings.Entities;

namespace ONEVO.Infrastructure.Persistence.Configurations.Calendar;

public class ExternalCalendarEventLinkConfiguration : IEntityTypeConfiguration<ExternalCalendarEventLink>
{
    public void Configure(EntityTypeBuilder<ExternalCalendarEventLink> builder)
    {
        builder.ToTable("external_calendar_event_links");
        builder.HasKey(l => l.Id);

        builder.Property(l => l.Provider).HasMaxLength(30).IsRequired();
        builder.Property(l => l.ExternalCalendarId).HasMaxLength(255).IsRequired();
        builder.Property(l => l.ExternalEventId).HasMaxLength(255).IsRequired();
        builder.Property(l => l.ExternalEtag).HasMaxLength(255);
        builder.Property(l => l.PushedFingerprint).HasMaxLength(64);
        builder.Property(l => l.SyncDirection).HasMaxLength(20).IsRequired();
        builder.Property(l => l.SyncStatus).HasMaxLength(20).IsRequired();

        builder.HasIndex(l => new { l.TenantId, l.CalendarEventId })
            .HasDatabaseName("ix_external_calendar_event_links_tenant_id_calendar_event_id");

        builder.HasIndex(l => new { l.TenantId, l.ExternalCalendarConnectionId })
            .HasDatabaseName("ix_external_calendar_event_links_tenant_id_connection_id");

        builder.HasIndex(l => new { l.ExternalCalendarConnectionId, l.ExternalEventId })
            .IsUnique()
            .HasDatabaseName("ix_external_calendar_event_links_one_per_connection_event");

        // A pushed meeting occurrence has one link per connection (spec 5.10).
        builder.HasIndex(l => new { l.MeetingTaskId, l.ExternalCalendarConnectionId })
            .IsUnique()
            .HasFilter("meeting_task_id IS NOT NULL")
            .HasDatabaseName("ux_external_calendar_event_links_meeting_task_connection");
        builder.HasOne<MeetingDetails>().WithMany().HasForeignKey(l => l.MeetingTaskId).OnDelete(DeleteBehavior.Restrict);
    }
}
