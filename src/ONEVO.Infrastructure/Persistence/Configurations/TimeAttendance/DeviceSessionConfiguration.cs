using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using ONEVO.Domain.Features.AgentGateway.Entities;
using ONEVO.Domain.Features.CoreHr.Entities;
using ONEVO.Domain.Features.TimeAttendance.Entities;

namespace ONEVO.Infrastructure.Persistence.Configurations.TimeAttendance;

public sealed class DeviceSessionConfiguration : IEntityTypeConfiguration<DeviceSession>
{
    public void Configure(EntityTypeBuilder<DeviceSession> builder)
    {
        builder.ToTable(
            "device_sessions",
            table =>
            {
                table.HasCheckConstraint(
                    "ck_device_sessions_end_order",
                    "session_end IS NULL OR session_end >= session_start");
                table.HasCheckConstraint(
                    "ck_device_sessions_minutes",
                    "active_minutes >= 0 AND idle_minutes >= 0");
                table.HasCheckConstraint(
                    "ck_device_sessions_active_percentage",
                    "active_percentage BETWEEN 0 AND 100");
            });
        builder.HasKey(session => session.Id);

        builder.Property(session => session.ActivePercentage).HasPrecision(5, 2);
        builder.Property(session => session.Version).IsRowVersion();

        builder.HasIndex(session => new { session.TenantId, session.DeviceId })
            .IsUnique()
            .HasFilter("session_end IS NULL");
        builder.HasIndex(session => new
        {
            session.TenantId,
            session.EmployeeId,
            session.SessionStart
        });

        builder.HasOne<Employee>()
            .WithMany()
            .HasForeignKey(session => session.EmployeeId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<RegisteredAgent>()
            .WithMany()
            .HasForeignKey(session => session.DeviceId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
