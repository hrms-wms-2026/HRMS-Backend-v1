using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using ONEVO.Domain.Features.TimeAttendance.Entities;

namespace ONEVO.Infrastructure.Persistence.Configurations.TimeAttendance;

public sealed class AttendanceRecordConfiguration : IEntityTypeConfiguration<AttendanceRecord>
{
    public void Configure(EntityTypeBuilder<AttendanceRecord> b)
    {
        b.ToTable("attendance_records"); b.HasKey(x => x.Id);
        b.Property(x => x.WorkTimeType).HasMaxLength(20); b.Property(x => x.ExpectedWorkArea).HasMaxLength(10);
        b.Property(x => x.ScheduleTimezone).HasMaxLength(50); b.Property(x => x.HolidayName).HasMaxLength(100);
        b.Property(x => x.AttendanceSource).HasMaxLength(20); b.Property(x => x.Status).HasMaxLength(30).IsRequired();
        b.HasIndex(x => new { x.TenantId, x.EmployeeId, x.Date }).IsUnique();
        b.HasIndex(x => new { x.TenantId, x.Date });
    }
}

public sealed class PresenceSessionConfiguration : IEntityTypeConfiguration<PresenceSession>
{
    public void Configure(EntityTypeBuilder<PresenceSession> b)
    {
        b.ToTable("presence_sessions"); b.HasKey(x => x.Id);
        b.Property(x => x.Source).HasMaxLength(20); b.Property(x => x.Status).HasMaxLength(20);
        b.HasIndex(x => new { x.TenantId, x.EmployeeId, x.Date }).IsUnique();
    }
}

public sealed class BreakRecordConfiguration : IEntityTypeConfiguration<BreakRecord>
{
    public void Configure(EntityTypeBuilder<BreakRecord> b)
    {
        b.ToTable("break_records"); b.HasKey(x => x.Id);
        b.Property(x => x.BreakType).HasMaxLength(30);
        b.Property<uint>("xmin")
            .HasColumnName("xmin")
            .IsRowVersion();
        b.HasIndex(x => new { x.TenantId, x.EmployeeId, x.BreakStart });
        b.HasIndex(x => new { x.TenantId, x.EmployeeId })
            .IsUnique()
            .HasFilter("break_end IS NULL")
            .HasDatabaseName("ux_break_records_one_open_per_employee");
    }
}
