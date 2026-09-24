using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using ONEVO.Domain.Features.WorkManagement.Objectives.Entities;
using ONEVO.Domain.Features.WorkManagement.ProjectInvitations.Entities;
using ONEVO.Domain.Features.WorkManagement.Projects.Entities;

namespace ONEVO.Infrastructure.Persistence.Configurations.WorkManagement;

public class ProjectMemberInvitationConfiguration : IEntityTypeConfiguration<ProjectMemberInvitation>
{
    public void Configure(EntityTypeBuilder<ProjectMemberInvitation> builder)
    {
        builder.ToTable("project_member_invitations");
        builder.HasKey(i => i.Id);
        builder.Property(i => i.Status).HasMaxLength(20).IsRequired();
        builder.Property(i => i.InviteType).HasMaxLength(20).IsRequired().HasDefaultValue(ProjectInvitationTypes.Member);

        builder.HasIndex(i => new { i.TenantId, i.InvitedEmployeeId, i.Status })
            .HasDatabaseName("ix_project_member_invitations_tenant_invited_employee_status");
        builder.HasIndex(i => new { i.TenantId, i.ProjectId, i.ObjectiveId, i.InvitedEmployeeId })
            .IsUnique()
            .HasFilter("status = 'pending'")
            .HasDatabaseName("ix_project_member_invitations_one_pending");
        builder.HasIndex(i => new { i.TenantId, i.ObjectiveId })
            .IsUnique()
            .HasFilter("status = 'pending' AND invite_type = 'leader'")
            .HasDatabaseName("ix_project_member_invitations_one_pending_leader");

        builder.HasOne<Project>().WithMany().HasForeignKey(i => i.ProjectId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<Objective>().WithMany().HasForeignKey(i => i.ObjectiveId).OnDelete(DeleteBehavior.Restrict);
    }
}
