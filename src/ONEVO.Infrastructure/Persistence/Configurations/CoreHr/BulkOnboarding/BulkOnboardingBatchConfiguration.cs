using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using ONEVO.Domain.Features.CoreHr.Entities;

namespace ONEVO.Infrastructure.Persistence.Configurations.CoreHr.BulkOnboarding;

public class BulkOnboardingBatchConfiguration : IEntityTypeConfiguration<BulkOnboardingBatch>
{
    public void Configure(EntityTypeBuilder<BulkOnboardingBatch> builder)
    {
        builder.ToTable("bulk_onboarding_batches");
        builder.HasKey(b => b.Id);

        builder.Property<uint>("xmin")
            .HasColumnName("xmin").HasColumnType("xid")
            .ValueGeneratedOnAddOrUpdate().IsConcurrencyToken();

        builder.Property(b => b.DefaultEmploymentType).HasMaxLength(30);
        builder.Property(b => b.ColumnMappingJson).HasColumnType("jsonb");
        builder.Property(b => b.OriginalFileName).HasMaxLength(255).IsRequired();
        builder.Property(b => b.Status).HasMaxLength(30).IsRequired();

        builder.HasIndex(b => new { b.TenantId, b.Status });

        builder.HasOne<ONEVO.Domain.Features.OrgStructure.Entities.LegalEntity>()
            .WithMany().HasForeignKey(b => b.LegalEntityId).OnDelete(DeleteBehavior.Restrict);
    }
}
