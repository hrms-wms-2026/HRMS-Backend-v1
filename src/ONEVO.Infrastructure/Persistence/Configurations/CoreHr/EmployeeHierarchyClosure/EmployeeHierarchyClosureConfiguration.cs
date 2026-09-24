using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using EmployeeHierarchyClosureEntity = ONEVO.Domain.Features.CoreHr.Entities.EmployeeHierarchyClosure;

namespace ONEVO.Infrastructure.Persistence.Configurations.CoreHr.EmployeeHierarchyClosure;

public class EmployeeHierarchyClosureConfiguration : IEntityTypeConfiguration<EmployeeHierarchyClosureEntity>
{
    public void Configure(EntityTypeBuilder<EmployeeHierarchyClosureEntity> builder)
    {
        builder.ToTable("employee_hierarchy_closure");
        builder.HasKey(c => new { c.TenantId, c.AncestorEmployeeId, c.DescendantEmployeeId });

        builder.Property(c => c.Depth).IsRequired();
        builder.Property(c => c.SourcePositionAssignmentId).IsRequired();
        builder.Property(c => c.GeneratedAt).IsRequired();

        builder.HasIndex(c => new { c.TenantId, c.DescendantEmployeeId });
        builder.HasIndex(c => new { c.TenantId, c.AncestorEmployeeId, c.Depth });
    }
}
