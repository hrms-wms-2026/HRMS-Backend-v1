using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using ONEVO.Domain.Features.InfrastructureModule.Entities;
using ONEVO.Domain.Features.OrgStructure.Entities;
using ONEVO.Domain.Features.TimeAttendance.Entities;

namespace ONEVO.Infrastructure.Persistence.Configurations.TimeAttendance;

public sealed class ClockInPolicyConfiguration : IEntityTypeConfiguration<ClockInPolicy>
{
    public void Configure(EntityTypeBuilder<ClockInPolicy> builder)
    {
        builder.ToTable(
            "clock_in_policies",
            table =>
            {
                table.HasCheckConstraint(
                    "ck_clock_in_policies_scope_type",
                    "scope_type IN ('full_company', 'department', 'position', 'employee')");
                table.HasCheckConstraint(
                    "ck_clock_in_policies_effective_dates",
                    "effective_to IS NULL OR effective_to >= effective_from");
                table.HasCheckConstraint(
                    "ck_clock_in_policies_radius",
                    "allowed_radius_meters IS NULL OR allowed_radius_meters BETWEEN 25 AND 50000");
                table.HasCheckConstraint(
                    "ck_clock_in_policies_either_source_rule",
                    "either_source_rule IN ('onsite', 'remote', 'employee_choice')");
                table.HasCheckConstraint(
                    "ck_clock_in_policies_field_photo_requirement",
                    "field_photo_requirement IN ('off', 'optional', 'required')");
            });
        builder.HasKey(policy => policy.Id);

        builder.Property(policy => policy.Name).HasMaxLength(120).IsRequired();
        builder.Property(policy => policy.ScopeType).HasMaxLength(30).IsRequired();
        builder.Property(policy => policy.DepartmentIds).HasColumnType("uuid[]");
        builder.Property(policy => policy.PositionIds).HasColumnType("uuid[]");
        builder.Property(policy => policy.EmployeeIds).HasColumnType("uuid[]");
        builder.Property(policy => policy.EitherSourceRule).HasMaxLength(30).IsRequired();
        builder.Property(policy => policy.FieldPhotoRequirement).HasMaxLength(20).IsRequired();
        builder.Property(policy => policy.NotificationRecipientResolver).HasMaxLength(50).IsRequired();

        builder.HasIndex(policy => new
        {
            policy.TenantId,
            policy.LegalEntityId,
            policy.IsActive,
            policy.EffectiveFrom
        });
        builder.HasIndex(policy => new { policy.TenantId, policy.ScopeType });

        builder.HasOne<LegalEntity>()
            .WithMany()
            .HasForeignKey(policy => policy.LegalEntityId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<User>()
            .WithMany()
            .HasForeignKey(policy => policy.CreatedById)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
