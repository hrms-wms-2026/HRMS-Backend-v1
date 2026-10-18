using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using ONEVO.Domain.Features.WorkManagement.Meetings.Entities;

namespace ONEVO.Infrastructure.Persistence.Configurations.WorkManagement.Meetings;

public class MeetingOccurrenceSuppressionConfiguration : IEntityTypeConfiguration<MeetingOccurrenceSuppression>
{
    public void Configure(EntityTypeBuilder<MeetingOccurrenceSuppression> builder)
    {
        builder.ToTable("meeting_occurrence_suppressions");
        builder.HasKey(s => new { s.SeriesId, s.OriginalStart });
        builder.Property(s => s.Reason).HasMaxLength(20).IsRequired();
        builder.HasIndex(s => new { s.TenantId, s.MovedToTaskId })
            .HasDatabaseName("ix_meeting_occurrence_suppressions_moved_to");
    }
}
