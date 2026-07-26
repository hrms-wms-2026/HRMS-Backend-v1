using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using ONEVO.Domain.Features.SharedPlatform.PaymentGateway.Entities;

namespace ONEVO.Infrastructure.Persistence.Configurations.DevPlatform.SystemConfig;

/// <summary>
/// EF configuration for payment_gateway_credentials.
/// Phase 1 canonical table. Secrets stored as bytea (AES-256 via IEncryptionService).
/// Only one active row per payment_gateway_config_id - enforced at service layer.
/// </summary>
public class PaymentGatewayCredentialConfiguration : IEntityTypeConfiguration<PaymentGatewayCredential>
{
    public void Configure(EntityTypeBuilder<PaymentGatewayCredential> builder)
    {
        builder.ToTable("payment_gateway_credentials");

        builder.HasKey(c => c.Id);

        builder.Property(c => c.PaymentGatewayConfigId)
            .IsRequired();

        // Encrypted bytea columns - never returned by API
        builder.Property(c => c.SecretEncrypted)
            .HasColumnType("bytea")
            .IsRequired();

        builder.Property(c => c.WebhookSecretEncrypted)
            .HasColumnType("bytea");

        builder.Property(c => c.EncryptionKeyVersion)
            .HasMaxLength(50)
            .IsRequired();

        builder.Property(c => c.CredentialVersion)
            .IsRequired();

        builder.Property(c => c.IsActive)
            .IsRequired();

        builder.Property(c => c.RotatedById)
            .IsRequired();

        builder.Property(c => c.RotatedAt)
            .IsRequired();

        builder.Property(c => c.DeactivatedById);
        builder.Property(c => c.DeactivatedAt);

        // Index for active-credential lookup - one active row per config
        builder.HasIndex(c => new { c.PaymentGatewayConfigId, c.IsActive });
    }
}
