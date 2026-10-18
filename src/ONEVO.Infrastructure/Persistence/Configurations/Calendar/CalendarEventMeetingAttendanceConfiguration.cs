using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using ONEVO.Domain.Features.Calendar.Entities;
using ONEVO.Domain.Features.WorkManagement.Meetings.Entities;

namespace ONEVO.Infrastructure.Persistence.Configurations.Calendar;

public class CalendarEventMeetingAttendanceConfiguration : IEntityTypeConfiguration<CalendarEventMeetingAttendance>
{
    public void Configure(EntityTypeBuilder<CalendarEventMeetingAttendance> builder)
    {
        builder.ToTable("calendar_event_meeting_attendances");
        builder.HasKey(a => a.Id);

        builder.Property(a => a.ExternalParticipantName).HasMaxLength(200);
        builder.Property(a => a.ExternalParticipantEmail).HasMaxLength(255);
        builder.Property(a => a.ParticipantKey).HasMaxLength(255);

        builder.HasIndex(a => new { a.TenantId, a.CalendarEventMeetingId })
            .HasDatabaseName("ix_calendar_event_meeting_attendances_tenant_id_meeting_id");

        // Phase 2 per-occurrence attendance: a record is upserted on (session, participant, joined_at) (spec 5.8).
        builder.HasIndex(a => new { a.AttendanceSessionId, a.ParticipantKey, a.JoinedAt })
            .IsUnique()
            .HasFilter("attendance_session_id IS NOT NULL")
            .HasDatabaseName("ux_calendar_event_meeting_attendances_session_participant_joined");
        builder.HasIndex(a => new { a.TenantId, a.OccurrenceTaskId })
            .HasDatabaseName("ix_calendar_event_meeting_attendances_occurrence");
        builder.HasOne<MeetingDetails>().WithMany().HasForeignKey(a => a.OccurrenceTaskId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<MeetingAttendanceSession>().WithMany().HasForeignKey(a => a.AttendanceSessionId).OnDelete(DeleteBehavior.Restrict);
    }
}
