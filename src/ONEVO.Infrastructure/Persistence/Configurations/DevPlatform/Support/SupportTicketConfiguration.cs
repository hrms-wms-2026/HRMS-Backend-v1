using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using ONEVO.Domain.Features.DevPlatform.PlatformAccess.Entities;
using ONEVO.Domain.Features.DevPlatform.Support.Entities;
using ONEVO.Domain.Features.InfrastructureModule.Entities;

namespace ONEVO.Infrastructure.Persistence.Configurations.DevPlatform.Support;

public sealed class SupportTicketConfiguration : IEntityTypeConfiguration<SupportTicket>
{
    public void Configure(EntityTypeBuilder<SupportTicket> builder)
    {
        builder.ToTable("support_tickets");
        builder.HasKey(t => t.Id);

        builder.Property(t => t.TenantId).HasColumnName("tenant_id");
        builder.Property(t => t.Subject).HasColumnName("subject").HasMaxLength(200).IsRequired();
        builder.Property(t => t.Description).HasColumnName("description").HasMaxLength(4000).IsRequired();
        builder.Property(t => t.Status).HasColumnName("status").HasMaxLength(20).IsRequired();
        builder.Property(t => t.Priority).HasColumnName("priority").HasMaxLength(20).IsRequired();
        builder.Property(t => t.Category).HasColumnName("category").HasMaxLength(100);
        builder.Property(t => t.CreatedByPlatformUserId).HasColumnName("created_by_platform_user_id");
        builder.Property(t => t.AssignedToPlatformUserId).HasColumnName("assigned_to_platform_user_id");
        builder.Property(t => t.CreatedAt).HasColumnName("created_at").IsRequired();
        builder.Property(t => t.UpdatedAt).HasColumnName("updated_at");
        builder.Property(t => t.ResolvedAt).HasColumnName("resolved_at");

        builder.HasIndex(t => t.TenantId);
        builder.HasIndex(t => t.Status);
        builder.HasIndex(t => t.Priority);
        builder.HasIndex(t => t.CreatedAt);

        builder.HasOne<Tenant>().WithMany().HasForeignKey(t => t.TenantId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<PlatformUser>().WithMany()
            .HasForeignKey(t => t.CreatedByPlatformUserId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<PlatformUser>().WithMany()
            .HasForeignKey(t => t.AssignedToPlatformUserId).OnDelete(DeleteBehavior.Restrict);

        builder.HasMany(t => t.Comments).WithOne(c => c.Ticket)
            .HasForeignKey(c => c.TicketId).OnDelete(DeleteBehavior.Cascade);
    }
}
