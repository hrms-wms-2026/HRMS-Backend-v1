using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using ONEVO.Domain.Features.Calendar.Entities;

namespace ONEVO.Infrastructure.Persistence.Configurations.Calendar;

public class ExternalCalendarEventLinkConfiguration : IEntityTypeConfiguration<ExternalCalendarEventLink>
{
    public void Configure(EntityTypeBuilder<ExternalCalendarEventLink> builder)
    {
        builder.ToTable("external_calendar_event_links");
        builder.HasKey(l => l.Id);

        builder.Property(l => l.Provider).HasMaxLength(30).IsRequired();
        builder.Property(l => l.ExternalCalendarId).HasMaxLength(255).IsRequired();
        builder.Property(l => l.ExternalEventId).HasMaxLength(255).IsRequired();
        builder.Property(l => l.ExternalEtag).HasMaxLength(255);
        builder.Property(l => l.SyncDirection).HasMaxLength(20).IsRequired();
        builder.Property(l => l.SyncStatus).HasMaxLength(20).IsRequired();

        builder.HasIndex(l => new { l.TenantId, l.CalendarEventId })
            .HasDatabaseName("ix_external_calendar_event_links_tenant_id_calendar_event_id");

        builder.HasIndex(l => new { l.TenantId, l.ExternalCalendarConnectionId })
            .HasDatabaseName("ix_external_calendar_event_links_tenant_id_connection_id");

        builder.HasIndex(l => new { l.ExternalCalendarConnectionId, l.ExternalEventId })
            .IsUnique()
            .HasDatabaseName("ix_external_calendar_event_links_one_per_connection_event");
    }
}
