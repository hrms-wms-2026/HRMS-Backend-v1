using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using ONEVO.Domain.Features.Monitoring.CheckIn.Entities;

namespace ONEVO.Infrastructure.Persistence.Configurations.Monitoring.CheckIn;

public class EmployeeCheckInConfiguration : IEntityTypeConfiguration<EmployeeCheckIn>
{
    public void Configure(EntityTypeBuilder<EmployeeCheckIn> builder)
    {
        builder.ToTable("employee_check_ins");
        builder.HasKey(e => e.Id);

        builder.Property(e => e.LocationAddress).HasMaxLength(500);
        builder.Property(e => e.DeviceSerialNumber).HasMaxLength(200);

        builder.HasOne(e => e.FaceScan)
               .WithOne()
               .HasForeignKey<EmployeeCheckIn>(e => e.FaceScanId)
               .IsRequired(false)
               .OnDelete(DeleteBehavior.SetNull);

        builder.HasIndex(e => new { e.TenantId, e.UserId, e.CheckedInAt });
        builder.HasIndex(e => new { e.TenantId, e.DeviceRegistrationId });
    }
}
