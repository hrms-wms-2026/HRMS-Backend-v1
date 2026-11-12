using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using ONEVO.Domain.Features.DevPlatform.PlatformAccess.Entities;
using ONEVO.Domain.Features.DevPlatform.Support.Entities;
using ONEVO.Domain.Features.InfrastructureModule.Entities;

namespace ONEVO.Infrastructure.Persistence.Configurations.DevPlatform.Support;

public sealed class SupportTicketAttachmentConfiguration : IEntityTypeConfiguration<SupportTicketAttachment>
{
    public void Configure(EntityTypeBuilder<SupportTicketAttachment> builder)
    {
        builder.ToTable("support_ticket_attachments", table =>
        {
            table.HasCheckConstraint(
                "ck_support_ticket_attachments_single_uploader",
                "(uploaded_by_platform_user_id IS NULL AND uploaded_by_user_id IS NOT NULL) OR (uploaded_by_platform_user_id IS NOT NULL AND uploaded_by_user_id IS NULL)");
            table.HasCheckConstraint(
                "ck_support_ticket_attachments_internal_is_platform",
                "is_internal = 0 OR uploaded_by_platform_user_id IS NOT NULL");
            table.HasCheckConstraint("ck_support_ticket_attachments_size", "size_bytes > 0");
        });
        builder.HasKey(x => x.Id);

        builder.Property(x => x.FileName).HasMaxLength(255).IsRequired();
        builder.Property(x => x.ContentType).HasMaxLength(150).IsRequired();
        builder.Property(x => x.SizeBytes).IsRequired();
        builder.Property(x => x.ObjectKey).HasMaxLength(500).IsRequired();
        builder.Property(x => x.IsInternal).IsRequired();
        builder.Property(x => x.UploadedByName).HasMaxLength(200).IsRequired();
        builder.Property(x => x.CreatedAt).IsRequired();

        builder.HasIndex(x => x.ObjectKey)
            .IsUnique()
            .HasDatabaseName("ux_support_ticket_attachments_object_key");
        builder.HasIndex(x => new { x.TenantId, x.TicketId, x.CreatedAt })
            .HasDatabaseName("ix_support_ticket_attachments_tenant_ticket_created");

        builder.HasOne<User>()
            .WithMany()
            .HasForeignKey(x => x.UploadedByUserId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<PlatformUser>()
            .WithMany()
            .HasForeignKey(x => x.UploadedByPlatformUserId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
