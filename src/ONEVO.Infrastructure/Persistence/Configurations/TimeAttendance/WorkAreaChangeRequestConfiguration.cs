using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using ONEVO.Domain.Features.CoreHr.Entities;
using ONEVO.Domain.Features.InfrastructureModule.Entities;
using ONEVO.Domain.Features.OrgStructure.Entities;
using ONEVO.Domain.Features.TimeAttendance.Entities;

namespace ONEVO.Infrastructure.Persistence.Configurations.TimeAttendance;

public sealed class WorkAreaChangeRequestConfiguration
    : IEntityTypeConfiguration<WorkAreaChangeRequest>
{
    public void Configure(EntityTypeBuilder<WorkAreaChangeRequest> builder)
    {
        builder.ToTable(
            "work_area_change_requests",
            table =>
            {
                table.HasCheckConstraint(
                    "ck_work_area_change_requests_current_area",
                    "current_expected_work_area IN ('onsite', 'remote', 'either', 'field')");
                table.HasCheckConstraint(
                    "ck_work_area_change_requests_requested_area",
                    "requested_work_area IN ('onsite', 'remote', 'either', 'field')");
                table.HasCheckConstraint(
                    "ck_work_area_change_requests_status",
                    "status IN ('pending', 'approved', 'rejected', 'cancelled')");
            });
        builder.HasKey(request => request.Id);

        builder.Property(request => request.CurrentExpectedWorkArea).HasMaxLength(10).IsRequired();
        builder.Property(request => request.RequestedWorkArea).HasMaxLength(10).IsRequired();
        builder.Property(request => request.Reason).HasColumnType("text").IsRequired();
        builder.Property(request => request.Status).HasMaxLength(20).IsRequired();
        builder.Property(request => request.ReviewComment).HasColumnType("text");
        builder.Property(request => request.Version).IsRowVersion();

        builder.HasIndex(request => new { request.TenantId, request.EmployeeId, request.Date });
        builder.HasIndex(request => new { request.TenantId, request.Status });
        builder.HasIndex(request => new { request.TenantId, request.EmployeeId, request.Date })
            .IsUnique()
            .HasFilter("status = 'pending'");

        builder.HasOne<Employee>()
            .WithMany()
            .HasForeignKey(request => request.EmployeeId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<LegalEntity>()
            .WithMany()
            .HasForeignKey(request => request.LegalEntityId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<User>()
            .WithMany()
            .HasForeignKey(request => request.ReviewedById)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
