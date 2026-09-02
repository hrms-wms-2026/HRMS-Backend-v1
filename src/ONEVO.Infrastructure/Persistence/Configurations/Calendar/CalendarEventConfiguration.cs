using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using ONEVO.Domain.Features.Calendar.Entities;

namespace ONEVO.Infrastructure.Persistence.Configurations.Calendar;

public class CalendarEventConfiguration : IEntityTypeConfiguration<CalendarEvent>
{
    public void Configure(EntityTypeBuilder<CalendarEvent> builder)
    {
        builder.ToTable("personal_calendar_events");
        builder.HasKey(e => e.Id);

        builder.Property(e => e.Title).HasMaxLength(200).IsRequired();
        builder.Property(e => e.SourceType).HasMaxLength(30).IsRequired();
        builder.Property(e => e.Color).HasMaxLength(7);
        builder.Property(e => e.Recurrence).HasMaxLength(20).IsRequired().HasDefaultValue(CalendarRecurrences.None);
        builder.Property(e => e.ExternalId).HasMaxLength(255);
        builder.Property(e => e.ExternalSource).HasMaxLength(30);
        builder.Property(e => e.Timezone).HasMaxLength(50);
        builder.Property(e => e.EventStatus).HasMaxLength(20);
        builder.Property(e => e.OrganizerName).HasMaxLength(200);
        builder.Property(e => e.OrganizerEmail).HasMaxLength(255);
        builder.Property(e => e.Location).HasMaxLength(500);
        builder.Property(e => e.MeetingLink).HasMaxLength(500);
        builder.Property(e => e.ExternalAttendeesJson).HasColumnName("external_attendees").HasColumnType("jsonb");

        builder.HasIndex(e => new { e.TenantId, e.StartDate, e.EndDate })
            .HasDatabaseName("ix_personal_calendar_events_tenant_id_start_date_end_date");

        builder.HasIndex(e => new { e.TenantId, e.CreatedById })
            .HasDatabaseName("ix_personal_calendar_events_tenant_id_created_by_id");

        builder.HasIndex(e => new { e.TenantId, e.RecurrenceParentId })
            .HasDatabaseName("ix_personal_calendar_events_tenant_id_recurrence_parent_id");

        builder.HasOne<CalendarEvent>()
            .WithMany()
            .HasForeignKey(e => e.RecurrenceParentId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
