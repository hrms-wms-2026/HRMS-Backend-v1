using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using ONEVO.Domain.Features.WorkManagement.CalendarEvents.Entities;

namespace ONEVO.Infrastructure.Persistence.Configurations.WorkManagement;

public sealed class CalendarEventActivityLogConfiguration : IEntityTypeConfiguration<CalendarEventActivityLog>
{
    public void Configure(EntityTypeBuilder<CalendarEventActivityLog> builder)
    {
        builder.ToTable("calendar_event_activity_logs");
        builder.HasKey(e => e.Id);
        builder.Property(e => e.Action).HasMaxLength(20).IsRequired();
        builder.Property(e => e.DetailsJson).HasColumnType("jsonb").IsRequired();
        builder.HasIndex(e => new { e.TenantId, e.CalendarEventId, e.PerformedAt })
            .HasDatabaseName("ix_calendar_event_activity_logs_tenant_event_performed_at");
        builder.HasOne<CalendarEvent>()
            .WithMany()
            .HasForeignKey(e => e.CalendarEventId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
