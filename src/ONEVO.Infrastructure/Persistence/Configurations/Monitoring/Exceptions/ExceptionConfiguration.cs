using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using MonitoringException = ONEVO.Domain.Features.Monitoring.Exceptions.Entities.Exception;

namespace ONEVO.Infrastructure.Persistence.Configurations.Monitoring.Exceptions;

public class ExceptionConfiguration : IEntityTypeConfiguration<MonitoringException>
{
    public void Configure(EntityTypeBuilder<MonitoringException> builder)
    {
        builder.ToTable("exceptions");
        builder.HasKey(e => e.Id);
        builder.Property(e => e.Type).HasConversion<string>().HasMaxLength(30);
        builder.Property(e => e.Status).HasConversion<string>().HasMaxLength(20);
        builder.Property(e => e.Title).HasMaxLength(200);
        builder.Property(e => e.Description).HasMaxLength(2000);
        builder.Property(e => e.MetadataJson)
            .HasColumnType("jsonb")
            .HasDefaultValue("{}");

        builder.HasIndex(e => new { e.TenantId, e.Status, e.DetectedAt })
            .HasDatabaseName("ix_exceptions_tenant_status_detected");

        builder.HasIndex(e => new { e.TenantId, e.EmployeeId, e.Type, e.Status })
            .HasDatabaseName("ix_exceptions_tenant_employee_type_status");
    }
}
