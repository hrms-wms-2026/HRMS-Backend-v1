using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using ONEVO.Domain.Features.CoreHr.Entities;

namespace ONEVO.Infrastructure.Persistence.Configurations.CoreHr.Onboarding;

public sealed class SeatIncreaseRequestConfiguration : IEntityTypeConfiguration<SeatIncreaseRequest>
{
    public void Configure(EntityTypeBuilder<SeatIncreaseRequest> builder)
    {
        builder.ToTable("seat_increase_requests");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Status).HasMaxLength(20).IsRequired();
        builder.Property(x => x.Reason).HasMaxLength(500);
        builder.Property(x => x.DecisionNote).HasMaxLength(500);
        builder.HasIndex(x => new { x.TenantId, x.Status });
        // One pending request per waiting draft.
        builder.HasIndex(x => new { x.TenantId, x.OnboardingDraftId })
            .IsUnique().HasFilter("status = 'pending' AND onboarding_draft_id IS NOT NULL");
        builder.HasOne<ONEVO.Domain.Features.CoreHr.Entities.OnboardingDraft>().WithMany().HasForeignKey(x => x.OnboardingDraftId).OnDelete(DeleteBehavior.SetNull);
        builder.HasOne<ONEVO.Domain.Features.InfrastructureModule.Entities.User>().WithMany().HasForeignKey(x => x.RequestedByUserId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<ONEVO.Domain.Features.InfrastructureModule.Entities.User>().WithMany().HasForeignKey(x => x.DecidedByUserId).OnDelete(DeleteBehavior.Restrict);
    }
}
