using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using ONEVO.Domain.Features.Auth.Entities;
using ONEVO.Domain.Features.InfrastructureModule.Entities;

namespace ONEVO.Infrastructure.Persistence.Configurations.Auth.Login;

public class TenantSessionExchangeChallengeConfiguration : IEntityTypeConfiguration<TenantSessionExchangeChallenge>
{
    public void Configure(EntityTypeBuilder<TenantSessionExchangeChallenge> builder)
    {
        builder.ToTable("tenant_session_exchange_challenges");
        builder.HasKey(c => c.Id);

        builder.Property(c => c.CodeHash).HasMaxLength(128).IsRequired();
        builder.Property(c => c.AuthOrigin).HasMaxLength(64).IsRequired();
        builder.Property(c => c.IpAddress).HasMaxLength(128);
        builder.Property(c => c.UserAgent).HasMaxLength(512);

        // Concurrency token makes consumption single-use: a competing UPDATE that already set
        // consumed_at causes DbUpdateConcurrencyException instead of a silent double-consume.
        builder.Property(c => c.ConsumedAt).IsConcurrencyToken();

        // Unique only among active (unconsumed) rows: TryConsumeAsync's hot lookup always filters
        // on consumed_at IS NULL, so this single partial index both enforces "no two live codes
        // collide" and serves that lookup - cheaper to maintain than a full-table unique index
        // plus a separate partial one. SHA-256 makes a collision with a dead row practically
        // impossible regardless.
        builder.HasIndex(c => c.CodeHash)
            .IsUnique()
            .HasDatabaseName("ix_tenant_session_exchange_challenges_code_hash_active")
            .HasFilter("consumed_at IS NULL");

        builder.HasIndex(c => new { c.TenantId, c.UserId });
        builder.HasIndex(c => c.ExpiresAt);

        builder.HasOne<Tenant>().WithMany().HasForeignKey(c => c.TenantId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<User>().WithMany().HasForeignKey(c => c.UserId).OnDelete(DeleteBehavior.Restrict);
    }
}
