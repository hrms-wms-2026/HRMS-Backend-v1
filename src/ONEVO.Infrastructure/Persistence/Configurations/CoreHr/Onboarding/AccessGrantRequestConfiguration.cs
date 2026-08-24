using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using ONEVO.Domain.Features.CoreHr.Entities;

namespace ONEVO.Infrastructure.Persistence.Configurations.CoreHr.Onboarding;

public sealed class AccessGrantRequestConfiguration : IEntityTypeConfiguration<AccessGrantRequest>
{
    public void Configure(EntityTypeBuilder<AccessGrantRequest> builder)
    {
        builder.ToTable("access_grant_requests");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.ActionType).HasMaxLength(30).IsRequired();
        builder.Property(x => x.ApprovalStatus).HasMaxLength(20).IsRequired();
        builder.Property(x => x.DecisionNote).HasMaxLength(500);
        // Concurrency token mapped to the PostgreSQL system column xmin - see
        // OnboardingDraftConfiguration.cs for the identical precedent and rationale. Declared
        // nullable (uint?, not uint) so EF does not emit a NOT NULL constraint: PostgreSQL always
        // populates its own xmin system column regardless of this metadata, but
        // OnboardingPersistenceRepositoryTests / DevSmokeTestTenantSeederTests exercise a real
        // non-PostgreSQL schema via InMemory/EnsureCreated, and that provider has no such system
        // column - a NOT NULL "xmin" there would reject every insert.
        builder.Property<uint?>("xmin")
            .HasColumnName("xmin")
            .HasColumnType("xid")
            .ValueGeneratedOnAddOrUpdate()
            .IsConcurrencyToken();
        // Keyed on OnboardingDraftId rather than EmployeeId: onboarding finalization submits
        // this request before the employee/user exist (see EmployeeId/UserId doc comment on the
        // entity), so the draft is the only stable correlation key while a request is pending.
        builder.HasIndex(x => new { x.TenantId, x.OnboardingDraftId, x.TargetPositionId, x.PositionAccessTemplateId })
            .IsUnique().HasFilter("approval_status = 'Pending'");
        builder.HasOne<ONEVO.Domain.Features.CoreHr.Entities.Employee>().WithMany().HasForeignKey(x => x.EmployeeId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<ONEVO.Domain.Features.InfrastructureModule.Entities.User>().WithMany().HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<ONEVO.Domain.Features.CoreHr.Entities.OnboardingDraft>().WithMany().HasForeignKey(x => x.OnboardingDraftId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<ONEVO.Domain.Features.InfrastructureModule.Entities.User>().WithMany().HasForeignKey(x => x.RequestedByUserId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<ONEVO.Domain.Features.InfrastructureModule.Entities.User>().WithMany().HasForeignKey(x => x.DecidedByUserId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<ONEVO.Domain.Features.OrgStructure.Entities.Position>().WithMany().HasForeignKey(x => x.TargetPositionId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<ONEVO.Domain.Features.OrgStructure.Entities.Department>().WithMany().HasForeignKey(x => x.TargetDepartmentId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<ONEVO.Domain.Features.OrgStructure.Entities.PositionAccessTemplate>().WithMany().HasForeignKey(x => x.PositionAccessTemplateId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<ONEVO.Domain.Features.Auth.Entities.Role>().WithMany().HasForeignKey(x => x.RequestedRoleId).OnDelete(DeleteBehavior.Restrict);
    }
}
