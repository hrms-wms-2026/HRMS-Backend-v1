using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using ONEVO.Domain.Features.DevPlatform.PlatformAccess.Entities;
using ONEVO.Domain.Features.InfrastructureModule.Entities;
using ONEVO.Domain.Features.SharedPlatform.Entities;

namespace ONEVO.Infrastructure.Persistence.Configurations.DevPlatform.Billing;

public class BillingAuditLogConfiguration : IEntityTypeConfiguration<BillingAuditLog>
{
    public void Configure(EntityTypeBuilder<BillingAuditLog> builder)
    {
        builder.ToTable("billing_audit_logs");
        builder.HasKey(l => l.Id);

        builder.Property(l => l.TenantId).HasColumnName("tenant_id");
        builder.Property(l => l.InvoiceId).HasColumnName("invoice_id");
        builder.Property(l => l.ActorAdminUserId).HasColumnName("actor_admin_user_id");
        builder.Property(l => l.Action).HasMaxLength(80).IsRequired();
        builder.Property(l => l.Message).HasMaxLength(1000).IsRequired();
        builder.Property(l => l.MetadataJson).HasColumnName("metadata_json").HasColumnType("jsonb");
        builder.Property(l => l.CreatedAt).HasColumnName("created_at").IsRequired();

        builder.HasIndex(l => new { l.InvoiceId, l.CreatedAt })
            .HasDatabaseName("ix_billing_audit_logs_invoice_id_created_at");

        builder.HasIndex(l => new { l.TenantId, l.CreatedAt })
            .HasDatabaseName("ix_billing_audit_logs_tenant_id_created_at");

        builder.HasOne<Tenant>()
            .WithMany()
            .HasForeignKey(l => l.TenantId)
            .OnDelete(DeleteBehavior.SetNull);

        builder.HasOne<SubscriptionInvoice>()
            .WithMany()
            .HasForeignKey(l => l.InvoiceId)
            .OnDelete(DeleteBehavior.SetNull);

        builder.HasOne<PlatformUser>()
            .WithMany()
            .HasForeignKey(l => l.ActorAdminUserId)
            .OnDelete(DeleteBehavior.SetNull);
    }
}
