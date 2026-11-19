using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using ONEVO.Domain.Features.SharedPlatform.Notifications.Entities;

namespace ONEVO.Infrastructure.Persistence.Configurations.SharedPlatform.Notifications;

public class NotificationDeliveryConfiguration : IEntityTypeConfiguration<NotificationDelivery>
{
    public void Configure(EntityTypeBuilder<NotificationDelivery> builder)
    {
        builder.ToTable("notification_deliveries");
        builder.HasKey(d => d.Id);
        builder.Ignore(d => d.IsTerminal);
        builder.Property(d => d.EventKey).HasMaxLength(100).IsRequired();
        builder.Property(d => d.Channel).HasConversion<int>();
        builder.Property(d => d.Title).HasMaxLength(255).IsRequired();
        builder.Property(d => d.EmailSubject).HasMaxLength(255);
        builder.Property(d => d.ActionKey).HasMaxLength(50).IsRequired();
        builder.Property(d => d.RelatedEntityType).HasMaxLength(40);
        builder.Property(d => d.Status).HasMaxLength(30).IsRequired();
        builder.Property(d => d.StatusReason).HasMaxLength(500);

        // One internal send per event, recipient and channel - retries and duplicate events collapse here.
        builder.HasIndex(d => new { d.TenantId, d.EventId, d.RecipientUserId, d.Channel }).IsUnique()
            .HasDatabaseName("ix_notification_deliveries_identity");
        builder.HasIndex(d => new { d.TenantId, d.Channel, d.RecipientUserId, d.Status })
            .HasDatabaseName("ix_notification_deliveries_tenant_channel_recipient_status");
    }
}
