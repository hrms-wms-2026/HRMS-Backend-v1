using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using ONEVO.Domain.Features.SharedPlatform.Notifications.Entities;

namespace ONEVO.Infrastructure.Persistence.Configurations.SharedPlatform.Notifications;

/// <summary>Global like notification_templates (product copy, no tenant column, no RLS).</summary>
public class NotificationTemplateVersionConfiguration : IEntityTypeConfiguration<NotificationTemplateVersion>
{
    public void Configure(EntityTypeBuilder<NotificationTemplateVersion> builder)
    {
        builder.ToTable("notification_template_versions");
        builder.HasKey(v => v.Id);
        builder.Property(v => v.Status).HasMaxLength(20).IsRequired();
        builder.Property(v => v.Revision).IsConcurrencyToken();
        builder.Property(v => v.InAppTitle).HasMaxLength(255).IsRequired();
        builder.Property(v => v.InAppBody).IsRequired();
        builder.Property(v => v.EmailSubject).HasMaxLength(255);
        builder.Property(v => v.TrayTitle).HasMaxLength(255);
        builder.Property(v => v.ActionKey).HasMaxLength(50).IsRequired();

        builder.HasOne<NotificationTemplate>().WithMany().HasForeignKey(v => v.TemplateId).OnDelete(DeleteBehavior.Restrict);
        builder.HasIndex(v => new { v.TemplateId, v.Version }).IsUnique()
            .HasDatabaseName("ix_notification_template_versions_template_id_version");
        builder.HasIndex(v => v.TemplateId).IsUnique()
            .HasFilter("status = 'draft'")
            .HasDatabaseName("ix_notification_template_versions_one_draft_per_template");
    }
}
