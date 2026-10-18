using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using ONEVO.Domain.Features.WorkManagement.Meetings.Entities;

namespace ONEVO.Infrastructure.Persistence.Configurations.WorkManagement.Meetings;

public class MeetingActionItemConfiguration : IEntityTypeConfiguration<MeetingActionItem>
{
    public void Configure(EntityTypeBuilder<MeetingActionItem> builder)
    {
        builder.ToTable("meeting_action_items");
        builder.HasKey(a => a.Id);

        // A task is an action item of at most one occurrence (spec 4.1, 5.4).
        builder.HasIndex(a => a.TaskId)
            .IsUnique()
            .HasDatabaseName("ux_meeting_action_items_task_id");
        builder.HasIndex(a => new { a.TenantId, a.OccurrenceTaskId })
            .HasDatabaseName("ix_meeting_action_items_occurrence");
    }
}
