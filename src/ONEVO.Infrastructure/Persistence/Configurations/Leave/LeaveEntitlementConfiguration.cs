using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using ONEVO.Domain.Features.CoreHr.Entities;
using ONEVO.Domain.Features.Leave.Entitlement.Entities;
using ONEVO.Domain.Features.Leave.Type.Entities;

namespace ONEVO.Infrastructure.Persistence.Configurations.Leave;

public class LeaveEntitlementConfiguration : IEntityTypeConfiguration<LeaveEntitlement>
{
    public void Configure(EntityTypeBuilder<LeaveEntitlement> builder)
    {
        builder.ToTable("leave_entitlements");
        builder.HasKey(e => e.Id);
        builder.Property(e => e.Source).HasMaxLength(10).IsRequired();
        builder.Property(e => e.TotalDays).HasColumnType("numeric(5,1)");
        builder.Property(e => e.UsedDays).HasColumnType("numeric(5,1)");
        builder.Property(e => e.PendingDays).HasColumnType("numeric(5,1)");
        builder.Property(e => e.CarriedForwardDays).HasColumnType("numeric(5,1)");

        builder.HasIndex(e => new { e.TenantId, e.EmployeeId, e.LeaveTypeId, e.Year })
            .IsUnique()
            .HasDatabaseName("ix_leave_entitlements_tenant_employee_type_year");

        builder.HasOne<LeaveType>().WithMany().HasForeignKey(e => e.LeaveTypeId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<Employee>().WithMany().HasForeignKey(e => e.EmployeeId).OnDelete(DeleteBehavior.Restrict);
    }
}
