using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using ONEVO.Domain.Features.TimeAttendance.Entities;

namespace ONEVO.Infrastructure.Persistence.Configurations.TimeAttendance;

public sealed class AttendanceCorrectionConfiguration : IEntityTypeConfiguration<AttendanceCorrection>
{
    public void Configure(EntityTypeBuilder<AttendanceCorrection> builder)
    {
        builder.ToTable("attendance_corrections");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.CorrectionType).HasMaxLength(30).IsRequired();
        builder.Property(x => x.WorkDate).IsRequired();
        builder.Property(x => x.Reason).HasMaxLength(255).IsRequired();
        builder.Property(x => x.Status).HasMaxLength(20).IsRequired();
        builder.Property(x => x.ApprovalRequired).HasColumnName("approval_required").IsRequired();
        builder.Property(x => x.Notes).HasColumnType("text");
        builder.Property(x => x.OriginalBreakJson).HasColumnType("jsonb");
        builder.Property(x => x.RequestedBreakJson).HasColumnType("jsonb");
        builder.Property(x => x.ReviewComment).HasColumnType("text");

        builder.HasIndex(x => new { x.TenantId, x.LegalEntityId, x.EmployeeId, x.CreatedAt })
            .HasDatabaseName("ix_attendance_corrections_tenant_legal_entity_employee_created_at");
        builder.HasIndex(x => new { x.TenantId, x.LegalEntityId, x.Status, x.CreatedAt })
            .HasDatabaseName("ix_attendance_corrections_tenant_legal_entity_status_created_at");
        builder.HasIndex(x => x.AttendanceRecordId)
            .HasDatabaseName("ix_attendance_corrections_attendance_record_id");
        builder.HasIndex(x => new { x.TenantId, x.EmployeeId, x.WorkDate, x.CorrectionType })
            .HasDatabaseName("ix_attendance_corrections_tenant_employee_work_date_type");

        // Attendance records are resolved before creation, so all supported requests have a
        // non-null record id. The partial unique index closes the duplicate-pending race.
        builder.HasIndex(x => new { x.TenantId, x.EmployeeId, x.WorkDate, x.CorrectionType })
            .IsUnique()
            .HasFilter("status = 'pending'")
            .HasDatabaseName("ux_attendance_corrections_pending_record_type");

        builder.HasOne<ONEVO.Domain.Features.CoreHr.Entities.Employee>()
            .WithMany().HasForeignKey(x => x.EmployeeId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<ONEVO.Domain.Features.OrgStructure.Entities.LegalEntity>()
            .WithMany().HasForeignKey(x => x.LegalEntityId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<PresenceSession>()
            .WithMany().HasForeignKey(x => x.PresenceSessionId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<AttendanceRecord>()
            .WithMany().HasForeignKey(x => x.AttendanceRecordId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<ONEVO.Domain.Features.InfrastructureModule.Entities.User>()
            .WithMany().HasForeignKey(x => x.RequestedById).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<ONEVO.Domain.Features.InfrastructureModule.Entities.User>()
            .WithMany().HasForeignKey(x => x.ReviewedById).OnDelete(DeleteBehavior.Restrict);
    }
}
