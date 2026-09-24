using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using ONEVO.Domain.Features.WorkManagement.CalendarEvents.Entities;
using ONEVO.Domain.Features.WorkManagement.Tasks.Entities;

namespace ONEVO.Infrastructure.Persistence.Configurations.WorkManagement;

public sealed class CalendarEventTaskConfiguration : IEntityTypeConfiguration<CalendarEventTask>
{
    public void Configure(EntityTypeBuilder<CalendarEventTask> builder)
    {
        builder.ToTable("calendar_event_tasks");
        builder.HasKey(e => e.Id);

        builder.HasIndex(e => new { e.CalendarEventId, e.TaskId })
            .IsUnique()
            .HasDatabaseName("ix_calendar_event_tasks_event_task");
        builder.HasIndex(e => e.TaskId)
            .HasDatabaseName("ix_calendar_event_tasks_task_id");

        builder.HasOne<CalendarEvent>()
            .WithMany()
            .HasForeignKey(e => e.CalendarEventId)
            .OnDelete(DeleteBehavior.Cascade);
        builder.HasOne<WorkTask>()
            .WithMany()
            .HasForeignKey(e => e.TaskId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
