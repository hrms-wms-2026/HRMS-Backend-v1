using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using ONEVO.Domain.Features.Configuration.Entities;
using ONEVO.Domain.Features.CoreHr.Entities;
using ONEVO.Domain.Features.InfrastructureModule.Entities;

namespace ONEVO.Infrastructure.Persistence.Configurations.Configuration;

public sealed class RemoteWorkLocationChangeRequestConfiguration
    : IEntityTypeConfiguration<RemoteWorkLocationChangeRequest>
{
    public void Configure(EntityTypeBuilder<RemoteWorkLocationChangeRequest> builder)
    {
        builder.ToTable(
            "remote_work_location_change_requests",
            table => table.HasCheckConstraint(
                "ck_remote_work_location_change_requests_status",
                "status IN ('pending', 'approved', 'rejected', 'captured', 'expired')"));
        builder.HasKey(request => request.Id);

        builder.Property(request => request.Reason).HasColumnType("text").IsRequired();
        builder.Property(request => request.Status).HasMaxLength(20).IsRequired();
        builder.Property(request => request.ReviewComment).HasColumnType("text");
        builder.Property(request => request.Version).IsRowVersion();

        builder.HasIndex(request => new { request.TenantId, request.EmployeeId })
            .IsUnique()
            .HasFilter("status = 'pending'");
        builder.HasIndex(request => new
        {
            request.TenantId,
            request.EmployeeId,
            request.RequestedAt
        });

        builder.HasOne<Employee>()
            .WithMany()
            .HasForeignKey(request => request.EmployeeId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<EmployeeRemoteWorkProfile>()
            .WithMany()
            .HasForeignKey(request => request.CurrentProfileId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<EmployeeRemoteWorkProfile>()
            .WithMany()
            .HasForeignKey(request => request.NewProfileId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<User>()
            .WithMany()
            .HasForeignKey(request => request.ReviewedById)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
