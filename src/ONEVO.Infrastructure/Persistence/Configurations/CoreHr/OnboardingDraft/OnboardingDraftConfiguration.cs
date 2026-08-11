using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using OnboardingDraftEntity = ONEVO.Domain.Features.CoreHr.Entities.OnboardingDraft;

namespace ONEVO.Infrastructure.Persistence.Configurations.CoreHr.OnboardingDraft;

public class OnboardingDraftConfiguration : IEntityTypeConfiguration<OnboardingDraftEntity>
{
    public void Configure(EntityTypeBuilder<OnboardingDraftEntity> builder)
    {
        builder.ToTable("onboarding_drafts");
        builder.HasKey(d => d.Id);

        // Concurrency token mapped to the PostgreSQL system column xmin, which already exists
        // on every table - not a real column to add. The migration this produces is hand-edited
        // to a no-op (see AddOnboardingDraftXminConcurrencyToken); the provider version pinned
        // here does not expose the UseXminAsConcurrencyToken() convenience method.
        builder.Property<uint>("xmin")
            .HasColumnName("xmin")
            .HasColumnType("xid")
            .ValueGeneratedOnAddOrUpdate()
            .IsConcurrencyToken();

        builder.Property(d => d.FirstName).HasMaxLength(100).IsRequired();
        builder.Property(d => d.LastName).HasMaxLength(100).IsRequired();
        builder.Property(d => d.WorkEmail).HasMaxLength(320).IsRequired();
        builder.Property(d => d.EmploymentType).HasMaxLength(30).IsRequired();
        builder.Property(d => d.EmployeeNumber).HasMaxLength(20);
        builder.Property(d => d.WorkModeId).IsRequired();
        builder.Property(d => d.Status).HasMaxLength(30).IsRequired();
        builder.Property(d => d.DraftReason).HasMaxLength(50);
        builder.Property(d => d.LastSavedStep).HasMaxLength(50).IsRequired();
        builder.Property(d => d.EditedTasksJson).HasColumnType("jsonb");

        builder.HasIndex(d => new { d.TenantId, d.Status });
        builder.HasIndex(d => new { d.TenantId, d.StartedById });

        builder.HasOne<ONEVO.Domain.Features.OrgStructure.Entities.LegalEntity>()
            .WithMany().HasForeignKey(d => d.LegalEntityId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<ONEVO.Domain.Features.OrgStructure.Entities.Department>()
            .WithMany().HasForeignKey(d => d.DepartmentId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<ONEVO.Domain.Features.OrgStructure.Entities.Position>()
            .WithMany().HasForeignKey(d => d.PositionId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<ONEVO.Domain.Lookups.WorkMode>()
            .WithMany().HasForeignKey(d => d.WorkModeId).OnDelete(DeleteBehavior.Restrict);
        // SelectedTemplateId: intentionally no HasOne - checklist_templates is not in this slice.
    }
}
