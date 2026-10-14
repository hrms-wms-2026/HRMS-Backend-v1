using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using ONEVO.Domain.Features.SharedPlatform.Entities;

namespace ONEVO.Infrastructure.Persistence.Configurations.SharedPlatform.Outbox;

public class OutboxMessageConfiguration : IEntityTypeConfiguration<OutboxMessage>
{
    public void Configure(EntityTypeBuilder<OutboxMessage> builder)
    {
        builder.ToTable("outbox_messages");

        builder.HasKey(m => m.Id);

        builder.Property(m => m.Type).HasMaxLength(150).IsRequired();
        builder.Property(m => m.EncryptedPayload).IsRequired();
        builder.Property(m => m.Status).HasMaxLength(20).IsRequired();
        builder.Property(m => m.LastError).HasMaxLength(2000);
        builder.Property(m => m.DeduplicationKey).HasMaxLength(200);
        builder.HasIndex(m => m.DeduplicationKey)
            .IsUnique()
            .HasFilter("deduplication_key IS NOT NULL")
            .HasDatabaseName("ix_outbox_messages_deduplication_key");

        // Worker poll: WHERE status = 'pending' AND next_attempt_at <= now ORDER BY created_at
        builder.HasIndex(m => new { m.Status, m.NextAttemptAt });
    }
}
