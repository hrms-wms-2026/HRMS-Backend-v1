using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using ONEVO.Domain.Features.Leave.Type.Entities;

namespace ONEVO.Infrastructure.Persistence.Configurations.Leave;

public class LeaveTypeConfiguration : IEntityTypeConfiguration<LeaveType>
{
    public void Configure(EntityTypeBuilder<LeaveType> builder)
    {
        builder.ToTable("leave_types");
        builder.HasKey(t => t.Id);
        builder.Property(t => t.Name).HasMaxLength(100).IsRequired();
        builder.Property(t => t.Code).HasMaxLength(20).IsRequired();
        builder.Property(t => t.Category).HasMaxLength(20).IsRequired();
        builder.Property(t => t.ApplicableGender).HasMaxLength(10).IsRequired();
        builder.Property(t => t.DefaultDaysPerYear).HasColumnType("numeric(5,1)");
        builder.Property(t => t.MaxCarryForwardDays).HasColumnType("numeric(5,1)");
        builder.Property(t => t.AcceptedDocumentTypes).HasColumnType("text[]");

        builder.HasIndex(t => t.TenantId).HasDatabaseName("ix_leave_types_tenant_id");

        builder.HasIndex(t => new { t.TenantId, t.Name })
            .IsUnique()
            .HasDatabaseName("ix_leave_types_tenant_id_name");

        builder.HasIndex(t => new { t.TenantId, t.Code })
            .IsUnique()
            .HasDatabaseName("ix_leave_types_tenant_id_code");
    }
}
