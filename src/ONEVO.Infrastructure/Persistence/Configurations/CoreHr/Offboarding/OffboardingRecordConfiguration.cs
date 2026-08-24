using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using ONEVO.Domain.Features.CoreHr.Entities;

namespace ONEVO.Infrastructure.Persistence.Configurations.CoreHr.Offboarding;

public sealed class OffboardingRecordConfiguration : IEntityTypeConfiguration<OffboardingRecord>
{
    public void Configure(EntityTypeBuilder<OffboardingRecord> builder)
    {
        builder.ToTable("offboarding_records");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Reason).HasMaxLength(30).IsRequired();
        builder.Property(x => x.KnowledgeRiskLevel).HasMaxLength(10).IsRequired();
        builder.Property(x => x.RehireEligibility).HasMaxLength(20);
        builder.Property(x => x.PenaltiesJson).HasColumnType("jsonb").HasDefaultValue("{}").IsRequired();
        builder.Property(x => x.Status).HasMaxLength(20).IsRequired();
        builder.HasIndex(x => new { x.TenantId, x.EmployeeId, x.Status });
        builder.HasIndex(x => new { x.TenantId, x.EmployeeId })
            .HasFilter("status IN ('initiated','in_progress')")
            .IsUnique();
        builder.HasOne<ONEVO.Domain.Features.CoreHr.Entities.Employee>().WithMany()
            .HasForeignKey(x => x.EmployeeId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<ChecklistTemplate>().WithMany()
            .HasForeignKey(x => x.ChecklistTemplateId).OnDelete(DeleteBehavior.Restrict);
    }
}
