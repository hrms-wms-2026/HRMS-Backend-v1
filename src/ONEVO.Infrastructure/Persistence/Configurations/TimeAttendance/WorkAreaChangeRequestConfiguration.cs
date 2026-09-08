using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using ONEVO.Domain.Features.TimeAttendance.Entities;

namespace ONEVO.Infrastructure.Persistence.Configurations.TimeAttendance;

public sealed class WorkAreaChangeRequestConfiguration : IEntityTypeConfiguration<WorkAreaChangeRequest>
{
    public void Configure(EntityTypeBuilder<WorkAreaChangeRequest> builder)
    {
        builder.ToTable("work_area_change_requests");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Date).HasColumnName("date").IsRequired();
        builder.Property(x => x.CurrentExpectedWorkArea).HasMaxLength(10).IsRequired();
        builder.Property(x => x.RequestedWorkArea).HasMaxLength(10).IsRequired();
        builder.Property(x => x.Reason).HasColumnType("text").IsRequired();
        builder.Property(x => x.Status).HasMaxLength(20).IsRequired().IsConcurrencyToken();
        builder.Property(x => x.ReviewComment).HasColumnType("text");

        builder.HasIndex(x => new { x.TenantId, x.Status })
            .HasDatabaseName("ix_work_area_change_requests_tenant_status");
        builder.HasIndex(x => new { x.TenantId, x.LegalEntityId, x.Status })
            .HasDatabaseName("ix_work_area_change_requests_tenant_legal_entity_status");
        builder.HasIndex(x => new { x.TenantId, x.EmployeeId, x.Date })
            .IsUnique()
            .HasFilter("status IN ('pending', 'approved')")
            .HasDatabaseName("ux_work_area_change_requests_active_employee_date");

        builder.HasOne<ONEVO.Domain.Features.CoreHr.Entities.Employee>()
            .WithMany().HasForeignKey(x => x.EmployeeId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<ONEVO.Domain.Features.OrgStructure.Entities.LegalEntity>()
            .WithMany().HasForeignKey(x => x.LegalEntityId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<ONEVO.Domain.Features.InfrastructureModule.Entities.User>()
            .WithMany().HasForeignKey(x => x.ReviewedById).OnDelete(DeleteBehavior.Restrict);
    }
}
