using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using ONEVO.Domain.Features.InfrastructureModule.Entities;
using ONEVO.Domain.Features.Storage.Quota.Entities;

namespace ONEVO.Infrastructure.Persistence.Configurations.Storage.Quota;

public class TenantStorageStatsConfiguration : IEntityTypeConfiguration<TenantStorageStats>
{
    public void Configure(EntityTypeBuilder<TenantStorageStats> builder)
    {
        builder.ToTable("tenant_storage_stats");

        builder.HasKey(t => t.TenantId);

        builder.Property(t => t.TenantId).HasColumnName("tenant_id").ValueGeneratedNever();
        builder.Property(t => t.UsedR2Bytes).HasColumnName("used_r2_bytes").IsRequired();
        builder.Property(t => t.UsedDbBytes).HasColumnName("used_db_bytes").IsRequired();
        builder.Property(t => t.ReservedR2Bytes).HasColumnName("reserved_r2_bytes").IsRequired();
        builder.Property(t => t.LastCalculatedAt).HasColumnName("last_calculated_at");
        builder.Property(t => t.UpdatedAt).HasColumnName("updated_at");

        // Inventory: tenant_id is PK AND FK -> tenants. Navigationless FK with
        // Restrict matches the tenant-owned dependent convention used across
        // this backend (e.g. MfaChallenge, TenantIntegrationCredential).
        builder.HasOne<Tenant>().WithMany().HasForeignKey(t => t.TenantId).OnDelete(DeleteBehavior.Restrict);
    }
}
