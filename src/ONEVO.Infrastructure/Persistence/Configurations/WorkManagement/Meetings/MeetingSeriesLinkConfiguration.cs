using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using ONEVO.Domain.Features.Calendar.Entities;
using ONEVO.Domain.Features.WorkManagement.Meetings.Entities;

namespace ONEVO.Infrastructure.Persistence.Configurations.WorkManagement.Meetings;

public class MeetingSeriesLinkConfiguration : IEntityTypeConfiguration<MeetingSeriesLink>
{
    public void Configure(EntityTypeBuilder<MeetingSeriesLink> builder)
    {
        builder.ToTable("meeting_series_links");
        builder.HasKey(l => l.Id);
        builder.Property(l => l.Origin).HasMaxLength(30).IsRequired();

        // At most one active link reference per series (spec 4.1, 5.7).
        builder.HasIndex(l => l.SeriesId)
            .IsUnique()
            .HasFilter("released_at IS NULL")
            .HasDatabaseName("ux_meeting_series_links_one_active_per_series");
        builder.HasIndex(l => new { l.SeriesId, l.CalendarEventMeetingId })
            .IsUnique()
            .HasDatabaseName("ux_meeting_series_links_series_link");
        // Reference counting: "is anyone still holding this link?"
        builder.HasIndex(l => new { l.TenantId, l.CalendarEventMeetingId })
            .HasFilter("released_at IS NULL")
            .HasDatabaseName("ix_meeting_series_links_active_by_link");

        builder.HasOne<MeetingSeries>().WithMany().HasForeignKey(l => l.SeriesId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<CalendarEventMeeting>().WithMany().HasForeignKey(l => l.CalendarEventMeetingId).OnDelete(DeleteBehavior.Restrict);
    }
}
