using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using ONEVO.Domain.Features.Calendar.Entities;

namespace ONEVO.Infrastructure.Persistence.Configurations.Calendar;

public class CalendarEventMeetingAttendanceConfiguration : IEntityTypeConfiguration<CalendarEventMeetingAttendance>
{
    public void Configure(EntityTypeBuilder<CalendarEventMeetingAttendance> builder)
    {
        builder.ToTable("calendar_event_meeting_attendances");
        builder.HasKey(a => a.Id);

        builder.Property(a => a.ExternalParticipantName).HasMaxLength(200);
        builder.Property(a => a.ExternalParticipantEmail).HasMaxLength(255);

        builder.HasIndex(a => new { a.TenantId, a.CalendarEventMeetingId })
            .HasDatabaseName("ix_calendar_event_meeting_attendances_tenant_id_meeting_id");
    }
}
