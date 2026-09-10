using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using ONEVO.Domain.Features.TimeAttendance.Entities;

namespace ONEVO.Infrastructure.Persistence.Configurations.TimeAttendance;

public sealed class DailyWorkLocationConfirmationConfiguration
    : IEntityTypeConfiguration<DailyWorkLocationConfirmation>
{
    public void Configure(EntityTypeBuilder<DailyWorkLocationConfirmation> builder)
    {
        builder.ToTable("daily_work_location_confirmations");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.LocationType).HasMaxLength(20).IsRequired();

        builder.HasIndex(x => new { x.TenantId, x.EmployeeId, x.WorkDate })
            .IsUnique()
            .HasDatabaseName("ux_daily_work_location_confirmations_tenant_employee_date");

        builder.HasOne<ONEVO.Domain.Features.CoreHr.Entities.Employee>()
            .WithMany().HasForeignKey(x => x.EmployeeId).OnDelete(DeleteBehavior.Cascade);
    }
}
