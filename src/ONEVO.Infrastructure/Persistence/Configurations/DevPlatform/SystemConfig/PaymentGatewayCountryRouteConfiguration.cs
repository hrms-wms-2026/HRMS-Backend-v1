using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using ONEVO.Domain.Features.SharedPlatform.PaymentGateway.Entities;

namespace ONEVO.Infrastructure.Persistence.Configurations.DevPlatform.SystemConfig;

/// <summary>
/// EF configuration for payment_gateway_country_routes.
/// Phase 1 canonical table.
/// Business rule: one active route per (country_code, environment) - enforced at service layer.
/// </summary>
public class PaymentGatewayCountryRouteConfiguration : IEntityTypeConfiguration<PaymentGatewayCountryRoute>
{
    public void Configure(EntityTypeBuilder<PaymentGatewayCountryRoute> builder)
    {
        builder.ToTable("payment_gateway_country_routes");

        builder.HasKey(r => r.Id);

        builder.Property(r => r.CountryCode)
            .HasMaxLength(2)
            .IsRequired();

        builder.Property(r => r.CountryNameSnapshot)
            .HasMaxLength(120);

        builder.Property(r => r.GatewayConfigId)
            .IsRequired();

        builder.Property(r => r.Environment)
            .HasMaxLength(20)
            .IsRequired();

        builder.Property(r => r.IsActive)
            .IsRequired();

        builder.Property(r => r.CreatedById);

        builder.Property(r => r.CreatedAt)
            .IsRequired();

        builder.Property(r => r.UpdatedAt)
            .IsRequired();

        // Supports: "resolve active route for country+environment" lookup (tenant provisioning Step 3)
        builder.HasIndex(r => new { r.CountryCode, r.Environment, r.IsActive });
    }
}
