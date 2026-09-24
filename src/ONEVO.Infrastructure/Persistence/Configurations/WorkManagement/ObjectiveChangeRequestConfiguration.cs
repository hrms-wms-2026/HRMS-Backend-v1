using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using ONEVO.Domain.Features.WorkManagement.ObjectiveChangeRequests.Entities;
using ONEVO.Domain.Features.WorkManagement.Objectives.Entities;

namespace ONEVO.Infrastructure.Persistence.Configurations.WorkManagement;

public class ObjectiveChangeRequestConfiguration : IEntityTypeConfiguration<ObjectiveChangeRequest>
{
    public void Configure(EntityTypeBuilder<ObjectiveChangeRequest> builder)
    {
        builder.ToTable("objective_change_requests");
        builder.HasKey(r => r.Id);
        builder.Property(r => r.RequestType).HasMaxLength(20).IsRequired();
        builder.Property(r => r.Status).HasMaxLength(20).IsRequired();
        builder.Property(r => r.PayloadJson).HasColumnType("jsonb");

        builder.HasIndex(r => new { r.TenantId, r.ObjectiveId, r.Status })
            .HasDatabaseName("ix_objective_change_requests_tenant_id_objective_id_status");
        builder.HasIndex(r => new { r.TenantId, r.ReportingManagerId, r.Status })
            .HasDatabaseName("ix_objective_change_requests_tenant_id_reporting_manager_id_status");

        // At most one pending request per Objective (design §6) - DB-level guarantee, not just
        // handler-level, via a partial unique index on the pending rows only.
        builder.HasIndex(r => new { r.TenantId, r.ObjectiveId })
            .IsUnique()
            .HasFilter("status = 'pending'")
            .HasDatabaseName("ix_objective_change_requests_one_pending_per_objective");

        builder.HasOne<Objective>()
            .WithMany()
            .HasForeignKey(r => r.ObjectiveId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
