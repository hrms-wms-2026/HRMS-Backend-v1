using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using ONEVO.Domain.Features.ActivityMonitoring.Entities;

namespace ONEVO.Infrastructure.Persistence.Configurations.ActivityMonitoring;

public class ActivityDailySummaryConfiguration : IEntityTypeConfiguration<ActivityDailySummary>
{
    public void Configure(EntityTypeBuilder<ActivityDailySummary> builder)
    {
        builder.ToTable("activity_daily_summary");
        builder.HasKey(s => s.Id);
        builder.Property(s => s.ActivePercentage).HasPrecision(5, 2);
        builder.Property(s => s.ActivityScore).HasPrecision(5, 2);
        builder.Property(s => s.DataCoveragePercentage).HasPrecision(5, 2);
        builder.Property(s => s.IntensityAvg).HasPrecision(5, 2);
        builder.Property(s => s.TopAppsJson).HasColumnType("jsonb").IsRequired();
        builder.HasIndex(s => new { s.TenantId, s.EmployeeId, s.Date }).IsUnique();
    }
}
