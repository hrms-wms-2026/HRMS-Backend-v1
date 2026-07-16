using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using ONEVO.Domain.Features.SharedPlatform.Entities;

namespace ONEVO.Infrastructure.Persistence.Configurations.SharedPlatform.Idempotency;

public class IdempotencyRecordConfiguration : IEntityTypeConfiguration<IdempotencyRecord>
{
    public void Configure(EntityTypeBuilder<IdempotencyRecord> builder)
    {
        builder.ToTable("idempotency_records");

        builder.HasKey(r => r.Id);

        builder.Property(r => r.Key).HasMaxLength(200).IsRequired();
        builder.Property(r => r.Scope).HasMaxLength(200).IsRequired();
        builder.Property(r => r.RequesterId).HasMaxLength(100).IsRequired();
        builder.Property(r => r.RequestHash).HasMaxLength(64).IsRequired();
        builder.Property(r => r.Status).HasMaxLength(20).IsRequired();

        // Concurrency guard: the second in-flight request with the same key hits
        // this constraint instead of executing the action twice.
        builder.HasIndex(r => new { r.Key, r.Scope, r.RequesterId }).IsUnique();

        builder.HasIndex(r => r.ExpiresAt);
    }
}
