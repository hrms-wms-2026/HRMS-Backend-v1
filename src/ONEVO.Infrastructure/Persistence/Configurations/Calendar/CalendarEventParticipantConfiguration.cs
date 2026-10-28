using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using ONEVO.Domain.Features.Calendar.Entities;
using ONEVO.Domain.Features.WorkManagement.Meetings.Entities;

namespace ONEVO.Infrastructure.Persistence.Configurations.Calendar;

public class CalendarEventParticipantConfiguration : IEntityTypeConfiguration<CalendarEventParticipant>
{
    public void Configure(EntityTypeBuilder<CalendarEventParticipant> builder)
    {
        builder.ToTable("calendar_event_participants");
        builder.HasKey(p => p.Id);

        builder.Property(p => p.ResponseStatus).HasMaxLength(30).IsRequired().HasDefaultValue(CalendarEventParticipantStatuses.Pending);

        builder.HasIndex(p => new { p.TenantId, p.EventId, p.EmployeeId })
            .IsUnique()
            .HasDatabaseName("ix_calendar_event_participants_one_row_per_employee");

        builder.HasIndex(p => new { p.TenantId, p.EmployeeId })
            .HasDatabaseName("ix_calendar_event_participants_tenant_id_employee_id");

        // Series attendees (meetings-as-tasks Phase 2): one row per employee per series, like the event index above.
        builder.HasIndex(p => new { p.TenantId, p.MeetingSeriesId, p.EmployeeId })
            .IsUnique()
            .HasFilter("meeting_series_id IS NOT NULL")
            .HasDatabaseName("ux_calendar_event_participants_series_employee");
        builder.HasOne<MeetingSeries>().WithMany().HasForeignKey(p => p.MeetingSeriesId).OnDelete(DeleteBehavior.Restrict);
    }
}
