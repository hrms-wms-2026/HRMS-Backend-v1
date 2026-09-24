using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using ONEVO.Domain.Features.OrgStructure.Entities;

namespace ONEVO.Infrastructure.Persistence.Configurations.OrgStructure;

public class PositionReportingHistoryConfiguration : IEntityTypeConfiguration<PositionReportingHistory>
{
    public void Configure(EntityTypeBuilder<PositionReportingHistory> builder)
    {
        builder.ToTable("position_reporting_history");
        builder.HasKey(h => h.Id);

        builder.Property(h => h.TenantId).IsRequired();
        builder.Property(h => h.PositionId).IsRequired();
        builder.Property(h => h.EffectiveFrom).IsRequired();
        builder.Property(h => h.ChangeReason).HasMaxLength(250);
        builder.Property(h => h.CreatedAt).IsRequired();

        builder.HasOne<Position>()
            .WithMany()
            .HasForeignKey(h => h.PositionId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne<Position>()
            .WithMany()
            .HasForeignKey(h => h.ReportsToPositionId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(h => h.TenantId).HasDatabaseName("ix_position_reporting_history_tenant_id");
        builder.HasIndex(h => h.PositionId).HasDatabaseName("ix_position_reporting_history_position_id");
        builder.HasIndex(h => h.ReportsToPositionId).HasDatabaseName("ix_position_reporting_history_reports_to_position_id");
        builder.HasIndex(h => new { h.TenantId, h.PositionId, h.EffectiveFrom, h.EffectiveTo })
            .HasDatabaseName("ix_position_reporting_history_tenant_position_effective");
    }
}
