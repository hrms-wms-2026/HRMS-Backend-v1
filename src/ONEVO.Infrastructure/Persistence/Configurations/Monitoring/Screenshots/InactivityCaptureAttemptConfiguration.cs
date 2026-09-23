using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using ONEVO.Domain.Features.Monitoring.Screenshots.Entities;
using ONEVO.Domain.Features.Monitoring.TrayActivation.Entities;

namespace ONEVO.Infrastructure.Persistence.Configurations.Monitoring.Screenshots;

public class InactivityCaptureAttemptConfiguration : IEntityTypeConfiguration<InactivityCaptureAttempt>
{
    public void Configure(EntityTypeBuilder<InactivityCaptureAttempt> builder)
    {
        builder.ToTable("inactivity_capture_attempts");
        builder.HasKey(e => e.Id);

        // Client-generated (Guid.NewGuid() in the Tray App) — never database-generated, so a
        // retried submit for the same attempt is a plain primary-key lookup, not a new insert.
        builder.Property(e => e.Id).ValueGeneratedNever();

        builder.Property(e => e.Outcome).HasMaxLength(30).IsRequired();
        builder.Property(e => e.FailureCode).HasMaxLength(50);
        builder.Property(e => e.PolicyVersion).HasMaxLength(64).IsRequired();

        builder.HasIndex(e => new { e.TenantId, e.EmployeeId, e.PromptedAt })
            .HasDatabaseName("ix_inactivity_capture_attempts_tenant_employee_prompted");

        builder.HasOne<MonitoringEvidenceAsset>()
            .WithMany()
            .HasForeignKey(e => e.EvidenceAssetId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne<TrayDeviceRegistration>()
            .WithMany()
            .HasForeignKey(e => e.AgentDeviceId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
