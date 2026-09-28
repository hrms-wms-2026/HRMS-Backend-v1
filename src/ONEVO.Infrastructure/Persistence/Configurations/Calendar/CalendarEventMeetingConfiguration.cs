using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using ONEVO.Domain.Features.Calendar.Entities;

namespace ONEVO.Infrastructure.Persistence.Configurations.Calendar;

public class CalendarEventMeetingConfiguration : IEntityTypeConfiguration<CalendarEventMeeting>
{
    public void Configure(EntityTypeBuilder<CalendarEventMeeting> builder)
    {
        builder.ToTable("calendar_event_meetings");
        builder.HasKey(m => m.Id);

        builder.Property(m => m.Provider).HasMaxLength(30).IsRequired();
        builder.Property(m => m.ExternalMeetingId).HasMaxLength(255).IsRequired();
        builder.Property(m => m.JoinUrl).HasMaxLength(500).IsRequired();
        builder.Property(m => m.OrganizerJoinUrl).HasMaxLength(500);
        builder.Property(m => m.PasscodeOrPin).HasMaxLength(50);
        builder.Property(m => m.Status).HasMaxLength(20).IsRequired();

        // One auto-generated meeting per event.
        builder.HasIndex(m => m.CalendarEventId)
            .IsUnique()
            .HasDatabaseName("ix_calendar_event_meetings_one_per_event");

        builder.HasIndex(m => new { m.TenantId, m.ExternalCalendarConnectionId })
            .HasDatabaseName("ix_calendar_event_meetings_tenant_id_connection_id");
    }
}
