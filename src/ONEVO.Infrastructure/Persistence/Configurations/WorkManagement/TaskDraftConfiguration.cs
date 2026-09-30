using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using ONEVO.Domain.Features.WorkManagement.Tasks.Entities;

namespace ONEVO.Infrastructure.Persistence.Configurations.WorkManagement;

public sealed class TaskDraftConfiguration : IEntityTypeConfiguration<TaskDraft>
{
    public void Configure(EntityTypeBuilder<TaskDraft> builder)
    {
        builder.ToTable("task_drafts");
        builder.HasKey(d => d.Id);
        builder.Property(d => d.Title).HasMaxLength(500).IsRequired();
        builder.Property(d => d.PayloadJson).HasColumnType("jsonb").IsRequired();
        builder.HasIndex(d => new { d.TenantId, d.OwnerUserId }).HasDatabaseName("ix_task_drafts_tenant_id_owner_user_id");
    }
}
