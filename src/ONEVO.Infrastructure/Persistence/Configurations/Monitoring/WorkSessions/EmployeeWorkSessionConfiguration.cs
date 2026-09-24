using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using ONEVO.Domain.Features.Monitoring.WorkSessions.Entities;

namespace ONEVO.Infrastructure.Persistence.Configurations.Monitoring.WorkSessions;

public class EmployeeWorkSessionConfiguration : IEntityTypeConfiguration<EmployeeWorkSession>
{
    public void Configure(EntityTypeBuilder<EmployeeWorkSession> builder)
    {
        builder.ToTable("employee_work_sessions");
        builder.HasKey(e => e.Id);

        builder.Property(e => e.ScheduleDisplay).HasMaxLength(100);

        builder.HasIndex(e => new { e.TenantId, e.UserId, e.ClockInAt });
        builder.HasIndex(e => new { e.TenantId, e.DeviceRegistrationId });
    }
}
