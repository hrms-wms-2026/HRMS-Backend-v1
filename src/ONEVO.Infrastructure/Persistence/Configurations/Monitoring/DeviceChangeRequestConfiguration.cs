using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using ONEVO.Domain.Features.Monitoring.TrayActivation.Entities;

namespace ONEVO.Infrastructure.Persistence.Configurations.Monitoring;

public sealed class DeviceChangeRequestConfiguration : IEntityTypeConfiguration<DeviceChangeRequest>
{
    public void Configure(EntityTypeBuilder<DeviceChangeRequest> builder)
    {
        builder.ToTable("device_change_requests");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.NewDeviceFingerprint).HasMaxLength(128).IsRequired();
        builder.Property(x => x.NewDeviceName).HasMaxLength(256).IsRequired();
        builder.Property(x => x.NewDeviceOs).HasMaxLength(64).IsRequired();
        builder.Property(x => x.Status).HasMaxLength(20).IsRequired().IsConcurrencyToken();
        builder.Property(x => x.ReviewComment).HasColumnType("text");

        builder.HasIndex(x => new { x.TenantId, x.Status })
            .HasDatabaseName("ix_device_change_requests_tenant_status");
        // At most one PENDING request per employee - a repeat mismatch attempt
        // updates the existing pending row instead of inserting a duplicate.
        builder.HasIndex(x => new { x.TenantId, x.EmployeeId })
            .IsUnique()
            .HasFilter("status = 'pending'")
            .HasDatabaseName("ux_device_change_requests_pending_employee");

        builder.HasOne<TrayDeviceRegistration>()
            .WithMany().HasForeignKey(x => x.CurrentDeviceRegistrationId).OnDelete(DeleteBehavior.SetNull);
        builder.HasOne<ONEVO.Domain.Features.InfrastructureModule.Entities.User>()
            .WithMany().HasForeignKey(x => x.ReviewedById).OnDelete(DeleteBehavior.Restrict);
    }
}
