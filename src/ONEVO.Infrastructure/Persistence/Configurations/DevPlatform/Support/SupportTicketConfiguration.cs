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
        builder.ToTable("support_tickets", table =>
        {
            table.HasCheckConstraint(
                "ck_support_tickets_single_creator",
                "(created_by_platform_user_id IS NULL AND created_by_user_id IS NOT NULL) OR (created_by_platform_user_id IS NOT NULL AND created_by_user_id IS NULL)");
        });
        builder.HasKey(x => x.Id);

        builder.Property(x => x.Subject).HasMaxLength(200).IsRequired();
        builder.Property(x => x.Description).HasMaxLength(10000).IsRequired();
        builder.Property(x => x.Status).HasMaxLength(20).IsRequired();
        builder.Property(x => x.Priority).HasMaxLength(20).IsRequired();
        builder.Property(x => x.Category).HasMaxLength(80);
        builder.Property(x => x.CreatedByName).HasMaxLength(200).IsRequired();
        builder.Property(x => x.CorrelationIds).HasMaxLength(1000);
        builder.Property(x => x.CreatedAt).IsRequired();
        builder.Property(x => x.AwaitingSupport).IsRequired();

        builder.HasIndex(x => x.AwaitingSupport)
            .HasDatabaseName("ix_support_tickets_awaiting_support")
            .HasFilter("[awaiting_support] = 1");
        builder.HasIndex(x => new { x.TenantId, x.Status, x.CreatedAt })
            .HasDatabaseName("ix_support_tickets_tenant_status_created");
        builder.HasIndex(x => new { x.Status, x.Priority, x.CreatedAt })
            .HasDatabaseName("ix_support_tickets_status_priority_created");
        builder.HasIndex(x => new { x.AssignedToPlatformUserId, x.Status })
            .HasDatabaseName("ix_support_tickets_assignee_status");

        builder.HasOne<Tenant>()
            .WithMany()
            .HasForeignKey(x => x.TenantId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<User>()
            .WithMany()
            .HasForeignKey(x => x.CreatedByUserId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<PlatformUser>()
            .WithMany()
            .HasForeignKey(x => x.CreatedByPlatformUserId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<PlatformUser>()
            .WithMany()
            .HasForeignKey(x => x.AssignedToPlatformUserId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasMany(x => x.Comments)
            .WithOne(x => x.Ticket)
            .HasForeignKey(x => x.TicketId)
            .OnDelete(DeleteBehavior.Cascade);
        builder.HasMany(x => x.Attachments)
            .WithOne(x => x.Ticket)
            .HasForeignKey(x => x.TicketId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
