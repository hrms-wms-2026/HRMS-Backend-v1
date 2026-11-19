using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using ONEVO.Domain.Features.Calendar.Entities;
using ONEVO.Domain.Features.WorkManagement.Meetings.Entities;

namespace ONEVO.Infrastructure.Persistence.Configurations.Calendar;

public class CalendarEventGuestConfiguration : IEntityTypeConfiguration<CalendarEventGuest>
{
    public void Configure(EntityTypeBuilder<CalendarEventGuest> builder)
    {
        builder.ToTable("calendar_event_guests");
        builder.HasKey(g => g.Id);

        builder.Property(g => g.Email).HasMaxLength(255).IsRequired();

        builder.HasIndex(g => new { g.TenantId, g.EventId, g.Email })
            .IsUnique()
            .HasDatabaseName("ix_calendar_event_guests_one_row_per_email");

        // Series guests (meetings-as-tasks Phase 2): one row per email per series, like the event index above.
        builder.HasIndex(g => new { g.TenantId, g.MeetingSeriesId, g.Email })
            .IsUnique()
            .HasFilter("meeting_series_id IS NOT NULL")
            .HasDatabaseName("ux_calendar_event_guests_series_email");
        builder.HasOne<MeetingSeries>().WithMany().HasForeignKey(g => g.MeetingSeriesId).OnDelete(DeleteBehavior.Restrict);
    }
}
