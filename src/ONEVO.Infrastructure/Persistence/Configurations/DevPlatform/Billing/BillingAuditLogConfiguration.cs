using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using ONEVO.Domain.Features.DevPlatform.PlatformAccess.Entities;
using ONEVO.Domain.Features.InfrastructureModule.Entities;
using ONEVO.Domain.Features.SharedPlatform.Entities;
using ONEVO.Infrastructure.Persistence.SqlServer;

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
        builder.Property(l => l.MetadataJson).HasColumnName("metadata_json").IsJsonColumn();
        builder.Property(l => l.CreatedAt).HasColumnName("created_at").IsRequired();

        builder.HasIndex(l => new { l.InvoiceId, l.CreatedAt })
            .HasDatabaseName("ix_billing_audit_logs_invoice_id_created_at");

        builder.HasIndex(l => new { l.TenantId, l.CreatedAt })
            .HasDatabaseName("ix_billing_audit_logs_tenant_id_created_at");

        // Audit rows are append-only: deleting a referenced tenant, invoice or admin must fail, never
        // null the audit column (a cascade is not permission-checked, so it would bypass the DENY).
        builder.HasOne<Tenant>()
            .WithMany()
            .HasForeignKey(l => l.TenantId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne<SubscriptionInvoice>()
            .WithMany()
            .HasForeignKey(l => l.InvoiceId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne<PlatformUser>()
            .WithMany()
            .HasForeignKey(l => l.ActorAdminUserId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
