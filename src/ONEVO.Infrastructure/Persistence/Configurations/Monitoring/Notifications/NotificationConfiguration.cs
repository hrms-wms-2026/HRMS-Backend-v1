using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using ONEVO.Domain.Features.Monitoring.Notifications.Entities;

namespace ONEVO.Infrastructure.Persistence.Configurations.Monitoring.Notifications;

public class NotificationConfiguration : IEntityTypeConfiguration<Notification>
{
    public void Configure(EntityTypeBuilder<Notification> builder)
    {
        builder.ToTable("notifications");
        builder.HasKey(e => e.Id);
        builder.Property(e => e.Type).HasConversion<string>().HasMaxLength(30);
        builder.Property(e => e.Title).HasMaxLength(200);
        builder.Property(e => e.Message).HasMaxLength(1000);

        builder.HasIndex(e => new { e.TenantId, e.EmployeeId, e.CreatedAt })
            .IsDescending(false, false, true)
            .HasDatabaseName("ix_notifications_tenant_employee_created");

        builder.HasIndex(e => new { e.TenantId, e.EmployeeId, e.Type, e.CreatedAt })
            .IsDescending(false, false, false, true)
            .HasDatabaseName("ix_notifications_tenant_employee_type_created");
    }
}
