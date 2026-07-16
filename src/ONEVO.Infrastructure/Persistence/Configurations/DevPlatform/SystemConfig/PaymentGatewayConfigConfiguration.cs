using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using ONEVO.Domain.Features.SharedPlatform.PaymentGateway.Entities;

namespace ONEVO.Infrastructure.Persistence.Configurations.DevPlatform.SystemConfig;

/// <summary>
/// EF configuration for payment_gateway_configs.
/// Phase 1 canonical table - columns match phase1-table-inventory.md exactly.
/// </summary>
public class PaymentGatewayConfigConfiguration : IEntityTypeConfiguration<PaymentGatewayConfig>
{
    public void Configure(EntityTypeBuilder<PaymentGatewayConfig> builder)
    {
        builder.ToTable("payment_gateway_configs");

        builder.HasKey(g => g.Id);

        builder.Property(g => g.GatewayKey)
            .HasMaxLength(80)
            .IsRequired();

        builder.HasIndex(g => g.GatewayKey)
            .IsUnique();

        builder.Property(g => g.Provider)
            .HasMaxLength(30)
            .IsRequired();

        builder.Property(g => g.Environment)
            .HasMaxLength(20)
            .IsRequired();

        builder.Property(g => g.DisplayName)
            .HasMaxLength(100)
            .IsRequired();

        builder.Property(g => g.LogoUrl)
            .HasMaxLength(500);

        builder.Property(g => g.PublicKey)
            .HasMaxLength(255);

        builder.Property(g => g.MerchantId)
            .HasMaxLength(100);

        builder.Property(g => g.WebhookUrl)
            .HasMaxLength(500);

        builder.Property(g => g.IsActive)
            .IsRequired();

        builder.Property(g => g.CreatedById);

        builder.Property(g => g.CreatedAt)
            .IsRequired();

        builder.Property(g => g.UpdatedAt)
            .IsRequired();

        // Navigations
        builder.HasMany(g => g.Credentials)
            .WithOne(c => c.GatewayConfig)
            .HasForeignKey(c => c.PaymentGatewayConfigId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasMany(g => g.CountryRoutes)
            .WithOne(r => r.GatewayConfig)
            .HasForeignKey(r => r.GatewayConfigId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
