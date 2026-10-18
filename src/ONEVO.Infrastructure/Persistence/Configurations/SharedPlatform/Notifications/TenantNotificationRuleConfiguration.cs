using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using ONEVO.Domain.Features.SharedPlatform.Notifications.Entities;

namespace ONEVO.Infrastructure.Persistence.Configurations.SharedPlatform.Notifications;

public class TenantNotificationRuleConfiguration : IEntityTypeConfiguration<TenantNotificationRule>
{
    public void Configure(EntityTypeBuilder<TenantNotificationRule> builder)
    {
        builder.ToTable("tenant_notification_rules");
        builder.HasKey(r => r.Id);
        builder.Property(r => r.EventKey).HasMaxLength(100).IsRequired();
        builder.Property(r => r.RecipientRule).HasMaxLength(50).IsRequired();
        builder.Property(r => r.EnabledChannels).HasConversion<int>();
        builder.Property(r => r.Revision).IsConcurrencyToken();
        builder.HasOne<NotificationTemplate>().WithMany().HasForeignKey(r => r.TemplateId).OnDelete(DeleteBehavior.Restrict);
        builder.HasIndex(r => new { r.TenantId, r.EventKey }).IsUnique()
            .HasDatabaseName("ix_tenant_notification_rules_tenant_id_event_key");
    }
}
