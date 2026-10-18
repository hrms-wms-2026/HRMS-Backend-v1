using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using ONEVO.Domain.Features.WorkManagement.Meetings.Entities;

namespace ONEVO.Infrastructure.Persistence.Configurations.WorkManagement.Meetings;

public class MeetingAttendanceSessionConfiguration : IEntityTypeConfiguration<MeetingAttendanceSession>
{
    public void Configure(EntityTypeBuilder<MeetingAttendanceSession> builder)
    {
        builder.ToTable("meeting_attendance_sessions");
        builder.HasKey(s => s.Id);
        builder.Property(s => s.ProviderSessionId).HasMaxLength(255).IsRequired();

        // A provider session is recorded once per link; one occurrence may have several sessions (spec 4.1).
        builder.HasIndex(s => new { s.CalendarEventMeetingId, s.ProviderSessionId })
            .IsUnique()
            .HasDatabaseName("ux_meeting_attendance_sessions_provider_session");
        builder.HasIndex(s => new { s.TenantId, s.OccurrenceTaskId })
            .HasDatabaseName("ix_meeting_attendance_sessions_occurrence");
    }
}
