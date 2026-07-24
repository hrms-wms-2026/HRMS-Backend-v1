using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using ONEVO.Domain.Features.Configuration.Entities;
using ONEVO.Domain.Features.CoreHr.Entities;
using ONEVO.Domain.Features.IdentityVerification.Entities;
using ONEVO.Domain.Features.InfrastructureModule.Entities;

namespace ONEVO.Infrastructure.Persistence.Configurations.Configuration;

public sealed class EmployeeRemoteWorkProfileConfiguration
    : IEntityTypeConfiguration<EmployeeRemoteWorkProfile>
{
    public void Configure(EntityTypeBuilder<EmployeeRemoteWorkProfile> builder)
    {
        builder.ToTable(
            "employee_remote_work_profiles",
            table => table.HasCheckConstraint(
                "ck_employee_remote_work_profiles_status",
                "status IN ('pending_capture', 'active', 'archived', 'rejected')"));
        builder.HasKey(profile => profile.Id);

        builder.Property(profile => profile.Status).HasMaxLength(20).IsRequired();
        builder.Property(profile => profile.PublicIp).HasColumnType("inet");
        builder.Property(profile => profile.WifiSsid).HasMaxLength(255);
        builder.Property(profile => profile.WifiBssidHash).HasMaxLength(100);
        builder.Property(profile => profile.GatewayMacHash).HasMaxLength(100);
        builder.Property(profile => profile.CoarseLocationJson).HasColumnType("jsonb");

        builder.HasIndex(profile => new { profile.TenantId, profile.EmployeeId })
            .IsUnique()
            .HasFilter("status = 'active'");
        builder.HasIndex(profile => new
        {
            profile.TenantId,
            profile.EmployeeId,
            profile.Status
        });

        builder.HasOne<Employee>()
            .WithMany()
            .HasForeignKey(profile => profile.EmployeeId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<VerificationRecord>()
            .WithMany()
            .HasForeignKey(profile => profile.VerificationRecordId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<User>()
            .WithMany()
            .HasForeignKey(profile => profile.ApprovedById)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
