using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using ONEVO.Domain.Features.DevPlatform.SystemConfig.IntegrationCatalog.Entities;
using ONEVO.Domain.Features.InfrastructureModule.Entities;
using ONEVO.Domain.Features.SharedPlatform.TenantIntegrations.Entities;

namespace ONEVO.Infrastructure.Persistence.Configurations.SharedPlatform;

public sealed class TenantIntegrationCredentialConfiguration : IEntityTypeConfiguration<TenantIntegrationCredential>
{
    public void Configure(EntityTypeBuilder<TenantIntegrationCredential> builder)
    {
        builder.ToTable(
            "tenant_integration_credentials",
            table => table.HasCheckConstraint(
                "ck_tenant_integration_credentials_status",
                "status IN ('connected', 'error', 'expired', 'disconnected', 'disabled')"));
        builder.HasKey(x => x.Id);
        builder.Property(x => x.IntegrationKey).HasMaxLength(50).IsRequired();
        builder.Property(x => x.AccessTokenEncrypted);
        builder.Property(x => x.RefreshTokenEncrypted);
        builder.Property(x => x.ScopesGranted).HasColumnType("text[]").IsRequired();
        builder.Property(x => x.ExternalAccountId).HasMaxLength(200);
        builder.Property(x => x.ExternalAccountName).HasMaxLength(200);
        builder.Property(x => x.Status).HasMaxLength(20).IsRequired();
        builder.Property(x => x.ConnectedAt).IsRequired();

        builder.HasIndex(x => new { x.TenantId, x.IntegrationKey }).IsUnique();
        builder.HasIndex(x => new { x.TenantId, x.Status });
        builder.HasIndex(x => x.IntegrationKey);

        builder.HasOne<Tenant>().WithMany().HasForeignKey(x => x.TenantId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<IntegrationCatalogEntry>().WithMany().HasForeignKey(x => x.IntegrationKey).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<User>().WithMany().HasForeignKey(x => x.ConnectedByUserId).OnDelete(DeleteBehavior.Restrict);
    }
}
