using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using ONEVO.Domain.Features.SharedPlatform.Notifications.Entities;

namespace ONEVO.Infrastructure.Persistence.Configurations.SharedPlatform.Notifications;

public class NotificationConfiguration : IEntityTypeConfiguration<Notification>
{
    public void Configure(EntityTypeBuilder<Notification> builder)
    {
        builder.ToTable("notifications");
        builder.HasKey(n => n.Id);
        builder.Property(n => n.TemplateCode).HasMaxLength(100).IsRequired();
        builder.Property(n => n.Title).HasMaxLength(255).IsRequired();
        builder.Property(n => n.RelatedEntityType).HasMaxLength(40);

        builder.HasIndex(n => new { n.TenantId, n.RecipientUserId, n.IsRead, n.CreatedAt })
            .HasDatabaseName("ix_notifications_tenant_id_recipient_user_id_is_read_created_at");
    }
}
