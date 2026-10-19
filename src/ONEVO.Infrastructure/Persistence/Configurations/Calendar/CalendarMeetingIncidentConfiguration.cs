using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using ONEVO.Domain.Features.Calendar.Entities;

namespace ONEVO.Infrastructure.Persistence.Configurations.Calendar;

public class CalendarMeetingIncidentConfiguration : IEntityTypeConfiguration<CalendarMeetingIncident>
{
    public void Configure(EntityTypeBuilder<CalendarMeetingIncident> builder)
    {
        builder.ToTable("calendar_meeting_incidents");
        builder.HasKey(i => i.Id);
        builder.Property(i => i.RuleCode).HasMaxLength(40).IsRequired();
        builder.Property(i => i.Confidence).HasMaxLength(20).IsRequired();
        builder.Property(i => i.EvidenceJson).HasColumnType("jsonb").IsRequired();
        // One incident per employee, rule, meeting occurrence (calendar doc §6.1); a removed one may be recorded again.
        builder.HasIndex(i => new { i.TenantId, i.EmployeeId, i.RuleCode, i.EventId, i.OccurrenceStart })
            .IsUnique()
            .HasFilter("is_deleted = false")
            .HasDatabaseName("ux_calendar_meeting_incidents_once");
        builder.HasIndex(i => new { i.TenantId, i.ExceptionId })
            .HasDatabaseName("ix_calendar_meeting_incidents_exception");
    }
}
