using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using ONEVO.Domain.Features.DevPlatform.SystemConfig.IntegrationCatalog.Entities;
using ONEVO.Domain.Features.InfrastructureModule.Entities;
using ONEVO.Domain.Features.SharedPlatform.TenantIntegrations.Entities;

namespace ONEVO.Infrastructure.Persistence.Configurations.SharedPlatform;

public sealed class UserIntegrationConnectionConfiguration
    : IEntityTypeConfiguration<UserIntegrationConnection>
{
    public void Configure(EntityTypeBuilder<UserIntegrationConnection> builder)
    {
        builder.ToTable(
            "user_integration_connections",
            table => table.HasCheckConstraint(
                "ck_user_integration_connections_status",
                "status IN ('connected', 'error', 'expired', 'disconnected')"));
        builder.HasKey(connection => connection.Id);

        builder.Property(connection => connection.IntegrationKey)
            .HasMaxLength(50)
            .IsRequired();
        builder.Property(connection => connection.ProviderUserId).HasMaxLength(200);
        builder.Property(connection => connection.ProviderUsername).HasMaxLength(200);
        builder.Property(connection => connection.ProviderEmail).HasMaxLength(320);
        builder.Property(connection => connection.ScopesGranted).HasColumnType("text[]");
        builder.Property(connection => connection.Status).HasMaxLength(20).IsRequired();
        builder.Property(connection => connection.ConnectedAt).IsRequired();
        builder.Property(connection => connection.CreatedAt).IsRequired();

        builder.HasIndex(connection => new
            {
                connection.TenantId,
                connection.UserId,
                connection.IntegrationKey
            })
            .IsUnique()
            .HasFilter("disconnected_at IS NULL");
        builder.HasIndex(connection => new { connection.TenantId, connection.UserId });
        builder.HasIndex(connection => new { connection.TenantId, connection.IntegrationKey });
        builder.HasIndex(connection => connection.Status);

        builder.HasOne<Tenant>()
            .WithMany()
            .HasForeignKey(connection => connection.TenantId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<User>()
            .WithMany()
            .HasForeignKey(connection => connection.UserId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<IntegrationCatalogEntry>()
            .WithMany()
            .HasForeignKey(connection => connection.IntegrationKey)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
