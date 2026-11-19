using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using ONEVO.Domain.Features.WorkManagement.Notifications.Entities;

namespace ONEVO.Infrastructure.Persistence.Configurations.WorkManagement;

public class WorkNotificationLogConfiguration : IEntityTypeConfiguration<WorkNotificationLog>
{
    public void Configure(EntityTypeBuilder<WorkNotificationLog> builder)
    {
        builder.ToTable("wm_notification_log");
        builder.HasKey(l => l.Id);
        builder.Property(l => l.Kind).HasMaxLength(20).IsRequired();
        builder.Property(l => l.ActionType).HasMaxLength(40).IsRequired();
        builder.Property(l => l.TargetType).HasMaxLength(20).IsRequired();
        builder.Property(l => l.TargetTitle).HasMaxLength(500).IsRequired();

        builder.HasIndex(l => new { l.TenantId, l.ProjectId, l.RecipientEmployeeId, l.CreatedAt })
            .HasDatabaseName("ix_wm_notification_log_tenant_id_project_id_recipient_created_at")
            .IsDescending(false, false, false, true);
    }
}
