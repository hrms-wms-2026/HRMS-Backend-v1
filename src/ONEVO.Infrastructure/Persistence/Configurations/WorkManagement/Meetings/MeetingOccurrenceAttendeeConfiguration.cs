using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using ONEVO.Domain.Features.CoreHr.Entities;
using ONEVO.Domain.Features.WorkManagement.Meetings.Entities;

namespace ONEVO.Infrastructure.Persistence.Configurations.WorkManagement.Meetings;

public class MeetingOccurrenceAttendeeConfiguration : IEntityTypeConfiguration<MeetingOccurrenceAttendee>
{
    public void Configure(EntityTypeBuilder<MeetingOccurrenceAttendee> builder)
    {
        builder.ToTable("meeting_occurrence_attendees");
        builder.HasKey(a => a.Id);
        builder.Property(a => a.AttendeeKind).HasMaxLength(20).IsRequired();
        builder.Property(a => a.Email).HasMaxLength(255);
        builder.Property(a => a.Change).HasMaxLength(10).IsRequired();
        builder.Property(a => a.ResponseStatus).HasMaxLength(30);

        // One row per employee per occurrence; the guest twin (occurrence_task_id, lower(email)) is an expression
        // index EF cannot model, created in AddMeetingWorkItemsFoundation (spec 4.1).
        builder.HasIndex(a => new { a.OccurrenceTaskId, a.EmployeeId })
            .IsUnique()
            .HasFilter("attendee_kind = 'employee'")
            .HasDatabaseName("ux_meeting_occurrence_attendees_employee");
        builder.HasIndex(a => new { a.TenantId, a.EmployeeId })
            .HasDatabaseName("ix_meeting_occurrence_attendees_tenant_id_employee_id");

        builder.HasOne<MeetingDetails>().WithMany().HasForeignKey(a => a.OccurrenceTaskId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<Employee>().WithMany().HasForeignKey(a => a.EmployeeId).OnDelete(DeleteBehavior.Restrict);
    }
}
