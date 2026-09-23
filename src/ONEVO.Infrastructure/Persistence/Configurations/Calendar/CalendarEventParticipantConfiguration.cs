using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using ONEVO.Domain.Features.Calendar.Entities;

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
    }
}
