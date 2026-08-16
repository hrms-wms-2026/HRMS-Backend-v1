using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using ONEVO.Domain.Features.DevPlatform.PlatformAccess.Entities;
using ONEVO.Domain.Features.DevPlatform.Support.Entities;

namespace ONEVO.Infrastructure.Persistence.Configurations.DevPlatform.Support;

public sealed class SupportTicketCommentConfiguration : IEntityTypeConfiguration<SupportTicketComment>
{
    public void Configure(EntityTypeBuilder<SupportTicketComment> builder)
    {
        builder.ToTable("support_ticket_comments");
        builder.HasKey(c => c.Id);

        builder.Property(c => c.TicketId).HasColumnName("ticket_id").IsRequired();
        builder.Property(c => c.AuthorPlatformUserId).HasColumnName("author_platform_user_id");
        builder.Property(c => c.Body).HasColumnName("body").HasMaxLength(4000).IsRequired();
        builder.Property(c => c.IsInternal).HasColumnName("is_internal").IsRequired();
        builder.Property(c => c.CreatedAt).HasColumnName("created_at").IsRequired();

        builder.HasIndex(c => c.TicketId);
        builder.HasIndex(c => c.CreatedAt);

        builder.HasOne<PlatformUser>().WithMany()
            .HasForeignKey(c => c.AuthorPlatformUserId).OnDelete(DeleteBehavior.Restrict);
    }
}
