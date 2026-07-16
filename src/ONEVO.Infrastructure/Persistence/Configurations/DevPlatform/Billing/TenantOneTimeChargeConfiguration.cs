using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using ONEVO.Domain.Features.SharedPlatform.Entities;

namespace ONEVO.Infrastructure.Persistence.Configurations.DevPlatform.Billing;

public class TenantOneTimeChargeConfiguration : IEntityTypeConfiguration<TenantOneTimeCharge>
{
    public void Configure(EntityTypeBuilder<TenantOneTimeCharge> builder)
    {
        builder.ToTable("tenant_one_time_charges");
        builder.HasKey(c => c.Id);
        builder.Property(c => c.TenantId).IsRequired();
        builder.Property(c => c.SetupOptionKey).HasMaxLength(100).IsRequired();
        builder.Property(c => c.Description).HasMaxLength(500).IsRequired();
        builder.Property(c => c.Amount).HasColumnType("decimal(12,2)").IsRequired();
        builder.Property(c => c.Currency).HasMaxLength(3).IsRequired();
        builder.Property(c => c.Status).HasMaxLength(20).IsRequired();
        builder.Property(c => c.ChargedOnce).IsRequired();
        builder.Property(c => c.CreatedAt).IsRequired();
        builder.Property(c => c.CreatedById);

        // Index for lookups; uniqueness enforced at service layer
        builder.HasIndex(c => new { c.TenantId, c.SetupOptionKey });
    }
}
