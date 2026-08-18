using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using ONEVO.Domain.Features.WorkManagement.Objectives.Entities;
using ONEVO.Domain.Features.WorkManagement.ProjectMembers.Entities;
using ONEVO.Domain.Features.WorkManagement.Projects.Entities;

namespace ONEVO.Infrastructure.Persistence.Configurations.WorkManagement;

public class ProjectMemberConfiguration : IEntityTypeConfiguration<ProjectMember>
{
    public void Configure(EntityTypeBuilder<ProjectMember> builder)
    {
        builder.ToTable("project_members");
        builder.HasKey(m => m.Id);
        builder.Property(m => m.MembershipSource).HasMaxLength(30).IsRequired();

        builder.HasIndex(m => new { m.TenantId, m.ProjectId, m.ObjectiveId, m.EmployeeId })
            .IsUnique()
            .HasDatabaseName("ix_project_members_tenant_project_objective_employee");
        builder.HasIndex(m => new { m.TenantId, m.EmployeeId, m.IsActive, m.ProjectId })
            .HasDatabaseName("ix_project_members_tenant_employee_active_project");
        builder.HasIndex(m => new { m.TenantId, m.ProjectId, m.ObjectiveId, m.IsActive })
            .HasDatabaseName("ix_project_members_tenant_project_objective_active");

        builder.HasOne<Project>().WithMany().HasForeignKey(m => m.ProjectId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<Objective>().WithMany().HasForeignKey(m => m.ObjectiveId).OnDelete(DeleteBehavior.Restrict);
    }
}
