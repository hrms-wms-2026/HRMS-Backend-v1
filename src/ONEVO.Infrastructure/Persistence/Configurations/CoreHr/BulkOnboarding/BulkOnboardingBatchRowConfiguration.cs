using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using ONEVO.Domain.Features.CoreHr.Entities;
using ONEVO.Infrastructure.Persistence.SqlServer;

namespace ONEVO.Infrastructure.Persistence.Configurations.CoreHr.BulkOnboarding;

public class BulkOnboardingBatchRowConfiguration : IEntityTypeConfiguration<BulkOnboardingBatchRow>
{
    public void Configure(EntityTypeBuilder<BulkOnboardingBatchRow> builder)
    {
        builder.ToTable("bulk_onboarding_batch_rows");
        builder.HasKey(r => r.Id);

        builder.Property<ulong>("xmin")
            .HasColumnName("xmin")
            .IsRowVersion();

        builder.Property(r => r.RawDataJson).IsJsonColumn().IsRequired();
        builder.Property(r => r.Status).HasMaxLength(30).IsRequired();
        builder.Property(r => r.ErrorMessage);

        builder.HasIndex(r => new { r.TenantId, r.BatchId, r.RowNumber }).IsUnique();
        builder.HasIndex(r => new { r.TenantId, r.Status });

        builder.HasOne<BulkOnboardingBatch>()
            .WithMany().HasForeignKey(r => r.BatchId).OnDelete(DeleteBehavior.Cascade);
        builder.HasOne<ONEVO.Domain.Features.CoreHr.Entities.OnboardingDraft>()
            .WithMany().HasForeignKey(r => r.OnboardingDraftId).OnDelete(DeleteBehavior.SetNull);
    }
}
