using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using ONEVO.Domain.Features.WorkManagement.CalendarEvents.Entities;
using ONEVO.Domain.Features.WorkManagement.Objectives.Entities;

namespace ONEVO.Infrastructure.Persistence.Configurations.WorkManagement;

public sealed class CalendarEventObjectiveConfiguration : IEntityTypeConfiguration<CalendarEventObjective>
{
    public void Configure(EntityTypeBuilder<CalendarEventObjective> builder)
    {
        builder.ToTable("calendar_event_objectives");
        builder.HasKey(e => e.Id);

        builder.HasIndex(e => new { e.CalendarEventId, e.ObjectiveId })
            .IsUnique()
            .HasDatabaseName("ix_calendar_event_objectives_event_objective");
        builder.HasIndex(e => e.ObjectiveId)
            .HasDatabaseName("ix_calendar_event_objectives_objective_id");

        builder.HasOne<CalendarEvent>()
            .WithMany()
            .HasForeignKey(e => e.CalendarEventId)
            .OnDelete(DeleteBehavior.Cascade);
        builder.HasOne<Objective>()
            .WithMany()
            .HasForeignKey(e => e.ObjectiveId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
