using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using ONEVO.Domain.Features.Monitoring.ActivityMonitoring.Entities;

namespace ONEVO.Infrastructure.Persistence.Configurations.Monitoring.ActivityMonitoring;

public class ActivityDailySummaryConfiguration : IEntityTypeConfiguration<ActivityDailySummary>
{
    public void Configure(EntityTypeBuilder<ActivityDailySummary> builder)
    {
        builder.ToTable("activity_daily_summary");
        builder.HasKey(e => e.Id);

        builder.Property(e => e.ActivePercentage).HasPrecision(5, 2);
        builder.Property(e => e.ActivityScore).HasPrecision(5, 2);
        builder.Property(e => e.DataCoveragePercentage).HasPrecision(5, 2);
        builder.Property(e => e.IntensityAvg).HasPrecision(5, 2);
        builder.Property(e => e.TopAppsJson)
            .HasColumnType("jsonb")
            .HasDefaultValue("[]");
        builder.Property(e => e.DataSource).HasMaxLength(50);

        builder.HasIndex(e => new { e.TenantId, e.EmployeeId, e.Date })
            .IsUnique()
            .HasDatabaseName("ux_activity_daily_summary_tenant_employee_date");

        builder.HasIndex(e => new { e.TenantId, e.EmployeeId, e.Date })
            .IsDescending(false, false, true)
            .HasDatabaseName("ix_activity_daily_summary_tenant_employee_date_desc");
    }
}
