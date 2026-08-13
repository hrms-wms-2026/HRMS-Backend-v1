using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using ONEVO.Domain.Features.Monitoring.Screenshots.Entities;
using ONEVO.Domain.Features.Monitoring.TrayActivation.Entities;
using ONEVO.Domain.Features.Monitoring.WorkSessions.Entities;

namespace ONEVO.Infrastructure.Persistence.Configurations.Monitoring.Screenshots;

public class InactivityCaptureAttemptConfiguration : IEntityTypeConfiguration<InactivityCaptureAttempt>
{
    public void Configure(EntityTypeBuilder<InactivityCaptureAttempt> builder)
    {
        builder.ToTable("inactivity_capture_attempts");
        builder.HasKey(e => e.Id);
        builder.Property(e => e.Id).ValueGeneratedNever();

        builder.Property(e => e.Outcome).HasMaxLength(50).IsRequired();
        builder.Property(e => e.FailureCode).HasMaxLength(100);
        builder.Property(e => e.PolicyVersion).HasMaxLength(64).IsRequired();

        builder.HasIndex(e => new { e.TenantId, e.EmployeeId, e.PromptedAt })
            .IsDescending(false, false, true)
            .HasDatabaseName("ix_inactivity_capture_attempts_tenant_employee_prompted");

        builder.HasOne<TrayDeviceRegistration>()
            .WithMany()
            .HasForeignKey(e => e.AgentDeviceId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne<EmployeeWorkSession>()
            .WithMany()
            .HasForeignKey(e => e.WorkSessionId)
            .OnDelete(DeleteBehavior.SetNull);

        builder.HasOne<MonitoringEvidenceAsset>()
            .WithMany()
            .HasForeignKey(e => e.EvidenceAssetId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
