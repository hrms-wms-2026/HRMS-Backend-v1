using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using ONEVO.Domain.Features.DevPlatform.SystemConfig.PlatformProviders.Entities;

namespace ONEVO.Infrastructure.Persistence.Configurations.DevPlatform.SystemConfig;

public sealed class PlatformProviderConfiguration : IEntityTypeConfiguration<PlatformProvider>
{
    private static readonly DateTimeOffset SeededAt =
        new(2026, 7, 24, 0, 0, 0, TimeSpan.Zero);

    public void Configure(EntityTypeBuilder<PlatformProvider> builder)
    {
        builder.ToTable(
            "platform_providers",
            table => table.HasCheckConstraint(
                "ck_platform_providers_provider_family",
                "provider_family IN ('oauth_app', 'transactional_email', 'infrastructure', 'object_storage', 'ai_verification', 'payment_gateway')"));

        builder.HasKey(provider => provider.Id);

        builder.Property(provider => provider.ProviderKey)
            .HasMaxLength(50)
            .IsRequired();

        builder.Property(provider => provider.DisplayName)
            .HasMaxLength(100)
            .IsRequired();

        builder.Property(provider => provider.ProviderFamily)
            .HasMaxLength(50)
            .IsRequired();

        builder.Property(provider => provider.IsActive)
            .HasDefaultValue(true)
            .IsRequired();

        builder.Property(provider => provider.CreatedAt)
            .IsRequired();

        builder.Property(provider => provider.UpdatedAt)
            .IsRequired();

        builder.HasIndex(provider => new
            {
                provider.ProviderFamily,
                provider.IsActive,
                provider.DisplayName
            });

        builder.HasData(
            Seed("10000000-0000-0000-0000-000000000001", "google", "Google", PlatformProviderFamilies.OAuthApp),
            Seed("10000000-0000-0000-0000-000000000002", "github", "GitHub", PlatformProviderFamilies.OAuthApp),
            Seed("10000000-0000-0000-0000-000000000003", "microsoft", "Microsoft", PlatformProviderFamilies.OAuthApp),
            Seed("10000000-0000-0000-0000-000000000004", "zoom", "Zoom", PlatformProviderFamilies.OAuthApp),
            Seed("10000000-0000-0000-0000-000000000005", "sendgrid", "SendGrid", PlatformProviderFamilies.TransactionalEmail),
            Seed("10000000-0000-0000-0000-000000000006", "resend", "Resend", PlatformProviderFamilies.TransactionalEmail),
            Seed("10000000-0000-0000-0000-000000000007", "cloudflare", "Cloudflare", PlatformProviderFamilies.Infrastructure),
            Seed("10000000-0000-0000-0000-000000000008", "cloudflare_r2", "Cloudflare R2", PlatformProviderFamilies.ObjectStorage),
            Seed("10000000-0000-0000-0000-000000000009", "aws_rekognition", "AWS Rekognition", PlatformProviderFamilies.AiVerification),
            Seed("10000000-0000-0000-0000-000000000010", "stripe", "Stripe", PlatformProviderFamilies.PaymentGateway),
            Seed("10000000-0000-0000-0000-000000000011", "payhere", "PayHere", PlatformProviderFamilies.PaymentGateway),
            Seed("10000000-0000-0000-0000-000000000012", "paddle", "Paddle", PlatformProviderFamilies.PaymentGateway));
    }

    private static PlatformProvider Seed(
        string id,
        string providerKey,
        string displayName,
        string providerFamily) =>
        new()
        {
            Id = Guid.Parse(id),
            ProviderKey = providerKey,
            DisplayName = displayName,
            ProviderFamily = providerFamily,
            IsActive = true,
            CreatedAt = SeededAt,
            UpdatedAt = SeededAt
        };
}
