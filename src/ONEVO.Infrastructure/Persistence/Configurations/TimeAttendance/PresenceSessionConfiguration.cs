using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using ONEVO.Domain.Features.CoreHr.Entities;
using ONEVO.Domain.Features.TimeAttendance.Entities;

namespace ONEVO.Infrastructure.Persistence.Configurations.TimeAttendance;

public sealed class PresenceSessionConfiguration : IEntityTypeConfiguration<PresenceSession>
{
    public void Configure(EntityTypeBuilder<PresenceSession> builder)
    {
        builder.ToTable(
            "presence_sessions",
            table =>
            {
                table.HasCheckConstraint(
                    "ck_presence_sessions_source",
                    "source IN ('biometric', 'agent', 'manual', 'mixed')");
                table.HasCheckConstraint(
                    "ck_presence_sessions_status",
                    "status IN ('present', 'absent', 'partial', 'on_leave')");
                table.HasCheckConstraint(
                    "ck_presence_sessions_minutes",
                    "total_present_minutes >= 0 AND total_break_minutes >= 0");
                table.HasCheckConstraint(
                    "ck_presence_sessions_seen_order",
                    "last_seen_at >= first_seen_at");
            });
        builder.HasKey(session => session.Id);

        builder.Property(session => session.Source).HasMaxLength(20).IsRequired();
        builder.Property(session => session.Status).HasMaxLength(20).IsRequired();
        builder.Property(session => session.Version).IsRowVersion();

        builder.HasIndex(session => new { session.TenantId, session.EmployeeId, session.Date })
            .IsUnique();
        builder.HasIndex(session => new { session.TenantId, session.Date, session.Status });

        builder.HasOne<Employee>()
            .WithMany()
            .HasForeignKey(session => session.EmployeeId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
