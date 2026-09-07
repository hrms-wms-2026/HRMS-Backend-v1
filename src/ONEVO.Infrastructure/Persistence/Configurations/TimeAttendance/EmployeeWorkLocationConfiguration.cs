using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using ONEVO.Domain.Features.TimeAttendance.Entities;

namespace ONEVO.Infrastructure.Persistence.Configurations.TimeAttendance;

public sealed class EmployeeWorkLocationConfiguration : IEntityTypeConfiguration<EmployeeWorkLocation>
{
    public void Configure(EntityTypeBuilder<EmployeeWorkLocation> builder)
    {
        builder.ToTable("employee_work_locations", table =>
        {
            table.HasCheckConstraint(
                "ck_employee_work_locations_latitude",
                "latitude BETWEEN -90 AND 90");
            table.HasCheckConstraint(
                "ck_employee_work_locations_longitude",
                "longitude BETWEEN -180 AND 180");
        });
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Latitude).IsRequired();
        builder.Property(x => x.Longitude).IsRequired();

        builder.HasIndex(x => new { x.TenantId, x.EmployeeId })
            .IsUnique()
            .HasDatabaseName("ux_employee_work_locations_tenant_employee");

        builder.HasOne<ONEVO.Domain.Features.CoreHr.Entities.Employee>()
            .WithMany().HasForeignKey(x => x.EmployeeId).OnDelete(DeleteBehavior.Cascade);
    }
}
