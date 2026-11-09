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
        builder.Property(e => e.TotalHours).HasColumnType("decimal(8,2)");
        builder.Property(e => e.UsedHours).HasColumnType("decimal(8,2)");
        builder.Property(e => e.PendingHours).HasColumnType("decimal(8,2)");
        builder.Property(e => e.CarriedForwardHours).HasColumnType("decimal(8,2)");

        // Crediting and requests both move these totals; the PostgreSQL xmin system column makes a
        // lost update fail instead of silently overwriting (nullable for non-PostgreSQL test DBs,
        // see EmployeeConfiguration).
        builder.Property<ulong?>("xmin")
            .HasColumnName("xmin")
            .IsRowVersion();

        builder.HasIndex(e => new { e.TenantId, e.EmployeeId, e.LeaveTypeId, e.Year })
            .IsUnique()
            .HasDatabaseName("ix_leave_entitlements_tenant_employee_type_year");

        builder.HasOne<LeaveType>().WithMany().HasForeignKey(e => e.LeaveTypeId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<Employee>().WithMany().HasForeignKey(e => e.EmployeeId).OnDelete(DeleteBehavior.Restrict);
    }
}
