using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using ONEVO.Domain.Features.TimeAttendance.Entities;

namespace ONEVO.Infrastructure.Persistence.Configurations.TimeAttendance;

public sealed class LocationChangeRequestConfiguration : IEntityTypeConfiguration<LocationChangeRequest>
{
    public void Configure(EntityTypeBuilder<LocationChangeRequest> builder)
    {
        builder.ToTable("location_change_requests", table =>
        {
            table.HasCheckConstraint(
                "ck_location_change_requests_latitude",
                "requested_latitude BETWEEN -90 AND 90");
            table.HasCheckConstraint(
                "ck_location_change_requests_longitude",
                "requested_longitude BETWEEN -180 AND 180");
        });
        builder.HasKey(x => x.Id);
        builder.Property(x => x.RequestedLatitude).IsRequired();
        builder.Property(x => x.RequestedLongitude).IsRequired();
        builder.Property(x => x.Reason).HasColumnType("text").IsRequired();
        builder.Property(x => x.Status).HasMaxLength(20).IsRequired().IsConcurrencyToken();
        builder.Property(x => x.ReviewComment).HasColumnType("text");

        builder.HasIndex(x => new { x.TenantId, x.Status })
            .HasDatabaseName("ix_location_change_requests_tenant_status");
        builder.HasIndex(x => new { x.TenantId, x.LegalEntityId, x.Status })
            .HasDatabaseName("ix_location_change_requests_tenant_legal_entity_status");
        builder.HasIndex(x => new { x.TenantId, x.EmployeeId })
            .IsUnique()
            .HasFilter("status IN ('pending', 'approved')")
            .HasDatabaseName("ux_location_change_requests_active_employee");

        builder.HasOne<ONEVO.Domain.Features.CoreHr.Entities.Employee>()
            .WithMany().HasForeignKey(x => x.EmployeeId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<ONEVO.Domain.Features.OrgStructure.Entities.LegalEntity>()
            .WithMany().HasForeignKey(x => x.LegalEntityId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<ONEVO.Domain.Features.InfrastructureModule.Entities.User>()
            .WithMany().HasForeignKey(x => x.ReviewedById).OnDelete(DeleteBehavior.Restrict);
    }
}
