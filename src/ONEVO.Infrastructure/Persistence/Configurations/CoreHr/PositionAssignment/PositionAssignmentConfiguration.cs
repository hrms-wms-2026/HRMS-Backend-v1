using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PositionAssignmentEntity = ONEVO.Domain.Features.CoreHr.Entities.PositionAssignment;

namespace ONEVO.Infrastructure.Persistence.Configurations.CoreHr.PositionAssignment;

public class PositionAssignmentConfiguration : IEntityTypeConfiguration<PositionAssignmentEntity>
{
    public void Configure(EntityTypeBuilder<PositionAssignmentEntity> builder)
    {
        builder.ToTable("position_assignments");
        builder.HasKey(pa => pa.Id);

        builder.Property(pa => pa.EmployeeId).IsRequired();
        builder.Property(pa => pa.PositionId).IsRequired();
        builder.Property(pa => pa.AssignmentKind).HasMaxLength(30).IsRequired();
        builder.Property(pa => pa.AssignmentStatus).HasMaxLength(20).IsRequired();
        builder.Property(pa => pa.EffectiveFrom).IsRequired();

        builder.HasIndex(pa => new { pa.TenantId, pa.EmployeeId });
        builder.HasIndex(pa => new { pa.TenantId, pa.PositionId, pa.AssignmentStatus });

        // Exactly one active PrimaryEmployment assignment per employee (partial unique index).
        builder.HasIndex(pa => pa.EmployeeId)
            .IsUnique()
            .HasFilter("assignment_kind = 'PrimaryEmployment' AND assignment_status = 'active'")
            .HasDatabaseName("ix_position_assignments_one_active_primary_per_employee");

        builder.HasOne<ONEVO.Domain.Features.CoreHr.Entities.Employee>()
            .WithMany()
            .HasForeignKey(pa => pa.EmployeeId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne<ONEVO.Domain.Features.OrgStructure.Entities.Position>()
            .WithMany()
            .HasForeignKey(pa => pa.PositionId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
