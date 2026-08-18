using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using ONEVO.Domain.Features.CoreHr.Entities;

namespace ONEVO.Infrastructure.Persistence.Configurations.CoreHr.Offboarding;

public sealed class OffboardingTaskBypassRequestConfiguration : IEntityTypeConfiguration<OffboardingTaskBypassRequest>
{
    public void Configure(EntityTypeBuilder<OffboardingTaskBypassRequest> builder)
    {
        builder.ToTable("offboarding_task_bypass_requests");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.BypassReason).HasMaxLength(500).IsRequired();
        builder.Property(x => x.PenaltyDescription).HasMaxLength(500);
        builder.Property(x => x.PriorTaskStatus).HasMaxLength(20).IsRequired();
        builder.Property(x => x.Status).HasMaxLength(20).IsRequired();
        builder.Property(x => x.DecisionComment).HasMaxLength(500);
        builder.HasIndex(x => new { x.TenantId, x.ApproverId, x.Status });
        builder.HasIndex(x => x.EmployeeChecklistTaskId)
            .HasFilter("status = 'pending'")
            .IsUnique();
        builder.HasOne<EmployeeChecklistTask>().WithMany()
            .HasForeignKey(x => x.EmployeeChecklistTaskId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<OffboardingRecord>().WithMany()
            .HasForeignKey(x => x.OffboardingRecordId).OnDelete(DeleteBehavior.Restrict);
    }
}
