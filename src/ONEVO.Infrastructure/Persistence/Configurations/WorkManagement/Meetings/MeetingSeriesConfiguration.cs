using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using ONEVO.Domain.Features.CoreHr.Entities;
using ONEVO.Domain.Features.WorkManagement.Meetings.Entities;
using ONEVO.Domain.Features.WorkManagement.Objectives.Entities;

namespace ONEVO.Infrastructure.Persistence.Configurations.WorkManagement.Meetings;

public class MeetingSeriesConfiguration : IEntityTypeConfiguration<MeetingSeries>
{
    public void Configure(EntityTypeBuilder<MeetingSeries> builder)
    {
        builder.ToTable("meeting_series");
        builder.HasKey(s => s.Id);
        builder.Property(s => s.Title).HasMaxLength(500).IsRequired();
        builder.Property(s => s.Location).HasMaxLength(500);
        builder.Property(s => s.Timezone).HasMaxLength(50).IsRequired();
        builder.Property(s => s.RecurrenceRule).HasMaxLength(500);
        builder.Property(s => s.CreationPath).HasMaxLength(20).IsRequired();
        builder.Property(s => s.SplitReason).HasMaxLength(20);
        builder.Property(s => s.Revision).IsConcurrencyToken();

        // One series per approved request: a replayed approval never creates a second meeting (spec 4.1, 5.2).
        builder.HasIndex(s => s.ApprovalRequestId)
            .IsUnique()
            .HasFilter("approval_request_id IS NOT NULL")
            .HasDatabaseName("ux_meeting_series_approval_request_id");
        builder.HasIndex(s => new { s.TenantId, s.ProjectId, s.ObjectiveId })
            .HasDatabaseName("ix_meeting_series_placement");
        builder.HasIndex(s => new { s.TenantId, s.OrganizerEmployeeId })
            .HasDatabaseName("ix_meeting_series_organizer");
        builder.HasIndex(s => new { s.TenantId, s.SplitFromSeriesId })
            .HasDatabaseName("ix_meeting_series_split_from");

        // Plain FKs let EF order inserts; AddMeetingWorkItemsFoundation adds the tenant-composite FKs on top
        // (the placement FK is (tenant_id, project_id, objective_id) -> objectives).
        builder.HasOne<Objective>().WithMany().HasForeignKey(s => s.ObjectiveId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<Employee>().WithMany().HasForeignKey(s => s.OrganizerEmployeeId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<MeetingSeries>().WithMany().HasForeignKey(s => s.SplitFromSeriesId).OnDelete(DeleteBehavior.Restrict);
    }
}
