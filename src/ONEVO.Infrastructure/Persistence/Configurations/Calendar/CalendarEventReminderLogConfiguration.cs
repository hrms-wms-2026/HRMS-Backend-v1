using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using ONEVO.Domain.Features.Calendar.Entities;

namespace ONEVO.Infrastructure.Persistence.Configurations.Calendar;

public class CalendarEventReminderLogConfiguration : IEntityTypeConfiguration<CalendarEventReminderLog>
{
    public void Configure(EntityTypeBuilder<CalendarEventReminderLog> builder)
    {
        builder.ToTable("calendar_event_reminders_sent");
        builder.HasKey(r => r.Id);
        builder.HasIndex(r => new { r.TenantId, r.EventId, r.OccurrenceStart, r.EmployeeId })
            .IsUnique()
            .HasDatabaseName("ux_calendar_event_reminders_sent_once");
    }
}
