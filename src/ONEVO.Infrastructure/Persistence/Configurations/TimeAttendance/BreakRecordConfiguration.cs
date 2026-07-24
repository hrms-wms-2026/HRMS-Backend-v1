using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using ONEVO.Domain.Features.CoreHr.Entities;
using ONEVO.Domain.Features.TimeAttendance.Entities;

namespace ONEVO.Infrastructure.Persistence.Configurations.TimeAttendance;

public sealed class BreakRecordConfiguration : IEntityTypeConfiguration<BreakRecord>
{
    public void Configure(EntityTypeBuilder<BreakRecord> builder)
    {
        builder.ToTable(
            "break_records",
            table =>
            {
                table.HasCheckConstraint(
                    "ck_break_records_type",
                    "break_type IN ('lunch', 'prayer', 'smoke', 'personal', 'other')");
                table.HasCheckConstraint(
                    "ck_break_records_end_order",
                    "break_end IS NULL OR break_end >= break_start");
            });
        builder.HasKey(record => record.Id);

        builder.Property(record => record.BreakType).HasMaxLength(30).IsRequired();
        builder.Property(record => record.Version).IsRowVersion();

        builder.HasIndex(record => new { record.TenantId, record.EmployeeId })
            .IsUnique()
            .HasFilter("break_end IS NULL");
        builder.HasIndex(record => new
        {
            record.TenantId,
            record.EmployeeId,
            record.BreakStart
        });

        builder.HasOne<Employee>()
            .WithMany()
            .HasForeignKey(record => record.EmployeeId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
