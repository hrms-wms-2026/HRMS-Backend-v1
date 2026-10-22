using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using ONEVO.Domain.Features.WorkManagement.Objectives.Entities;

namespace ONEVO.Infrastructure.Persistence.Configurations.WorkManagement;

public class ModuleHourSummaryConfiguration : IEntityTypeConfiguration<ModuleHourSummary>
{
    public void Configure(EntityTypeBuilder<ModuleHourSummary> builder)
    {
        builder.ToTable("wm_module_hour_summaries");
        builder.HasKey(s => s.Id);
        // Id is the Module id, supplied by the trigger - never generated.
        builder.Property(s => s.Id).ValueGeneratedNever();
        builder.Property(s => s.NotStartedHours).HasColumnType("numeric(18,2)");
        builder.Property(s => s.InProgressHours).HasColumnType("numeric(18,2)");
        builder.Property(s => s.CompletedHours).HasColumnType("numeric(18,2)");

        builder.HasIndex(s => new { s.TenantId, s.ProjectId })
            .HasDatabaseName("ix_wm_module_hour_summaries_tenant_id_project_id");
    }
}
