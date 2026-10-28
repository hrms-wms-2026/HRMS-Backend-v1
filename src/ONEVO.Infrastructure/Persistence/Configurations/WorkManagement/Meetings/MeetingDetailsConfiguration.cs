using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using ONEVO.Domain.Features.Calendar.Entities;
using ONEVO.Domain.Features.WorkManagement.Meetings.Entities;
using ONEVO.Domain.Features.WorkManagement.Tasks.Entities;

namespace ONEVO.Infrastructure.Persistence.Configurations.WorkManagement.Meetings;

public class MeetingDetailsConfiguration : IEntityTypeConfiguration<MeetingDetails>
{
    public void Configure(EntityTypeBuilder<MeetingDetails> builder)
    {
        builder.ToTable("meeting_details");
        builder.HasKey(d => d.TaskId);
        builder.Property(d => d.TaskId).ValueGeneratedNever();
        builder.Property(d => d.Location).HasMaxLength(500);
        builder.Property(d => d.AttendanceStatus).HasMaxLength(30);
        builder.Property(d => d.AttendanceLastError).HasMaxLength(40);
        builder.Property(d => d.Revision).IsConcurrencyToken();

        // The occurrence key: a real (NOT DEFERRABLE) unique constraint, so materialization can use
        // ON CONFLICT ON CONSTRAINT (spec 5.6). An alternate key also stops EF from ever updating it.
        builder.HasAlternateKey(d => new { d.SeriesId, d.OriginalStart })
            .HasName("uq_meeting_details_series_original_start");
        builder.HasIndex(d => new { d.TenantId, d.StartAt })
            .HasDatabaseName("ix_meeting_details_tenant_id_start_at");
        builder.HasIndex(d => new { d.TenantId, d.AttendanceNextCheckAt })
            .HasFilter("attendance_finalized_at IS NULL AND attendance_next_check_at IS NOT NULL")
            .HasDatabaseName("ix_meeting_details_attendance_due");
        builder.HasIndex(d => new { d.TenantId, d.CalendarEventMeetingId })
            .HasDatabaseName("ix_meeting_details_calendar_event_meeting");

        // Plain FKs let EF order inserts; the migration adds the tenant-composite FKs on top.
        builder.HasOne<WorkTask>().WithOne().HasForeignKey<MeetingDetails>(d => d.TaskId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<MeetingSeries>().WithMany().HasForeignKey(d => d.SeriesId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<CalendarEventMeeting>().WithMany().HasForeignKey(d => d.CalendarEventMeetingId).OnDelete(DeleteBehavior.Restrict);
    }
}
