using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using ONEVO.Domain.Features.Calendar.Entities;

namespace ONEVO.Infrastructure.Persistence.Configurations.Calendar;

public class CalendarEventParticipantRsvpHistoryConfiguration : IEntityTypeConfiguration<CalendarEventParticipantRsvpHistory>
{
    public void Configure(EntityTypeBuilder<CalendarEventParticipantRsvpHistory> builder)
    {
        builder.ToTable("calendar_event_participant_rsvp_history");
        builder.HasKey(h => h.Id);
        builder.Property(h => h.FromStatus).HasMaxLength(30).IsRequired();
        builder.Property(h => h.ToStatus).HasMaxLength(30).IsRequired();
        builder.Property(h => h.Reason).HasMaxLength(2000);
        builder.HasIndex(h => new { h.TenantId, h.ParticipantId, h.ChangedAt })
            .HasDatabaseName("ix_calendar_event_participant_rsvp_history_participant");
    }
}
