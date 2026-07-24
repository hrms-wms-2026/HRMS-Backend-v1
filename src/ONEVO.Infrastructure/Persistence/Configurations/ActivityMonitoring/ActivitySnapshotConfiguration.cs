using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using ONEVO.Domain.Features.ActivityMonitoring.Entities;

namespace ONEVO.Infrastructure.Persistence.Configurations.ActivityMonitoring;

public class ActivitySnapshotConfiguration : IEntityTypeConfiguration<ActivitySnapshot>
{
    public void Configure(EntityTypeBuilder<ActivitySnapshot> builder)
    {
        builder.ToTable("activity_snapshots");
        builder.HasKey(s => s.Id);
        builder.Property(s => s.ForegroundProcessName).HasMaxLength(100).IsRequired();
        builder.Property(s => s.IntensityScore).HasPrecision(5, 2);
        builder.HasIndex(s => new { s.TenantId, s.EmployeeId, s.CapturedAt });
        builder.HasIndex(s => new { s.TenantId, s.CapturedAt });
    }
}
