using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using ONEVO.Domain.Features.DevPlatform.PlatformAccess.Entities;
using ONEVO.Domain.Features.DevPlatform.Support.Entities;
using ONEVO.Domain.Features.InfrastructureModule.Entities;

namespace ONEVO.Infrastructure.Persistence.Configurations.DevPlatform.Support;

public sealed class SupportTicketCommentConfiguration : IEntityTypeConfiguration<SupportTicketComment>
{
    public void Configure(EntityTypeBuilder<SupportTicketComment> builder)
    {
        builder.ToTable("support_ticket_comments", table =>
        {
            table.HasCheckConstraint(
                "ck_support_ticket_comments_single_author",
                "(author_platform_user_id IS NULL AND author_user_id IS NOT NULL) OR (author_platform_user_id IS NOT NULL AND author_user_id IS NULL)");
            // Only platform staff write internal notes: a tenant-authored comment is always visible to both sides.
            table.HasCheckConstraint(
                "ck_support_ticket_comments_internal_is_platform",
                "is_internal = 0 OR author_platform_user_id IS NOT NULL");
        });
        builder.HasKey(x => x.Id);

        builder.Property(x => x.AuthorName).HasMaxLength(200).IsRequired();
        builder.Property(x => x.Body).HasMaxLength(10000).IsRequired();
        builder.Property(x => x.IsInternal).IsRequired();
        builder.Property(x => x.CreatedAt).IsRequired();

        builder.HasIndex(x => new { x.TenantId, x.TicketId, x.CreatedAt })
            .HasDatabaseName("ix_support_ticket_comments_tenant_ticket_created");

        builder.HasOne<User>()
            .WithMany()
            .HasForeignKey(x => x.AuthorUserId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<PlatformUser>()
            .WithMany()
            .HasForeignKey(x => x.AuthorPlatformUserId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
