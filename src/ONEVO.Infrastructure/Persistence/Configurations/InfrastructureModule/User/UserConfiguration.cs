using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using UserEntity = ONEVO.Domain.Features.InfrastructureModule.Entities.User;

namespace ONEVO.Infrastructure.Persistence.Configurations.InfrastructureModule.User;

public class UserConfiguration : IEntityTypeConfiguration<UserEntity>
{
    public void Configure(EntityTypeBuilder<UserEntity> builder)
    {
        builder.ToTable("users");
        builder.HasKey(u => u.Id);
        builder.Property(u => u.Email).HasMaxLength(255).IsRequired();
        builder.Property(u => u.NormalizedEmail)
            .HasMaxLength(255)
            .HasColumnType("character varying(255)")
            .HasColumnName("normalized_email")
            .HasComputedColumnSql("lower(trim(email))", stored: true)
            .IsRequired();
        builder.Property(u => u.PasswordHash).HasMaxLength(255).IsRequired();
        builder.Property(u => u.FirstName).HasMaxLength(100).IsRequired();
        builder.Property(u => u.LastName).HasMaxLength(100).IsRequired();
        builder.Property(u => u.PasswordResetTokenHash).HasMaxLength(128);
        builder.HasIndex(u => new { u.TenantId, u.Email }).IsUnique();

        // Case-insensitive per-tenant uniqueness guarantee. The plain { TenantId, Email } index above
        // is kept for compatibility with any existing exact-match lookups; this is the real guarantee.
        builder.HasIndex(u => new { u.TenantId, u.NormalizedEmail })
            .IsUnique()
            .HasDatabaseName("ix_users_tenant_id_normalized_email");

        // Base-domain candidate lookup index: leads with normalized_email (the function's WHERE
        // clause), includes TenantId/Id so the scan can be index-only, and is filtered to the rows
        // the function actually selects (active, not deleted) to keep the index small.
        builder.HasIndex(u => new { u.NormalizedEmail, u.TenantId, u.Id })
            .HasDatabaseName("ix_users_normalized_email_active_lookup")
            .HasFilter("is_active = true AND is_deleted = false");
    }
}
