using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using ONEVO.Domain.Features.SharedPlatform.Notifications.Entities;

namespace ONEVO.Infrastructure.Persistence.Configurations.SharedPlatform.Notifications;

public class NotificationTemplateConfiguration : IEntityTypeConfiguration<NotificationTemplate>
{
    public void Configure(EntityTypeBuilder<NotificationTemplate> builder)
    {
        builder.ToTable("notification_templates");
        builder.HasKey(t => t.Id);
        builder.Property(t => t.Code).HasMaxLength(100).IsRequired();
        builder.Property(t => t.InAppTitleTemplate).HasMaxLength(255).IsRequired();
        builder.HasIndex(t => t.Code).IsUnique().HasDatabaseName("ix_notification_templates_one_per_code");
    }
}
