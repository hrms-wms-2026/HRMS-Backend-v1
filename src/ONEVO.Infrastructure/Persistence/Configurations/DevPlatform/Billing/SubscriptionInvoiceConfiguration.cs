using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using ONEVO.Domain.Features.InfrastructureModule.Entities;
using ONEVO.Domain.Features.SharedPlatform.Entities;

namespace ONEVO.Infrastructure.Persistence.Configurations.DevPlatform.Billing;

public class SubscriptionInvoiceConfiguration : IEntityTypeConfiguration<SubscriptionInvoice>
{
    public void Configure(EntityTypeBuilder<SubscriptionInvoice> builder)
    {
        builder.ToTable("subscription_invoices");
        builder.HasKey(i => i.Id);

        builder.Property(i => i.TenantId).HasColumnName("tenant_id").IsRequired();
        builder.Property(i => i.TenantSubscriptionId).HasColumnName("tenant_subscription_id");
        builder.Property(i => i.InvoiceNumber).HasColumnName("invoice_number").HasMaxLength(50).IsRequired();
        builder.Property(i => i.ExternalInvoiceId).HasColumnName("external_invoice_id").HasMaxLength(100);
        builder.Property(i => i.GatewayProvider).HasColumnName("gateway_provider").HasMaxLength(50);
        builder.Property(i => i.Status).HasMaxLength(20).IsRequired();
        builder.Property(i => i.Currency).HasMaxLength(3).IsRequired();
        builder.Property(i => i.SubtotalAmount).HasColumnName("subtotal_amount").HasColumnType("decimal(12,2)").IsRequired();
        builder.Property(i => i.TaxAmount).HasColumnName("tax_amount").HasColumnType("decimal(12,2)").IsRequired();
        builder.Property(i => i.DiscountAmount).HasColumnName("discount_amount").HasColumnType("decimal(12,2)").IsRequired();
        builder.Property(i => i.TotalAmount).HasColumnName("total_amount").HasColumnType("decimal(12,2)").IsRequired();
        builder.Property(i => i.PeriodStart).HasColumnName("period_start");
        builder.Property(i => i.PeriodEnd).HasColumnName("period_end");
        builder.Property(i => i.IssuedAt).HasColumnName("issued_at");
        builder.Property(i => i.DueAt).HasColumnName("due_at");
        builder.Property(i => i.PaidAt).HasColumnName("paid_at");
        builder.Property(i => i.VoidedAt).HasColumnName("voided_at");
        builder.Property(i => i.CreatedAt).HasColumnName("created_at").IsRequired();
        builder.Property(i => i.UpdatedAt).HasColumnName("updated_at");

        builder.HasIndex(i => i.InvoiceNumber)
            .IsUnique()
            .HasDatabaseName("ix_subscription_invoices_invoice_number");

        builder.HasIndex(i => i.ExternalInvoiceId)
            .IsUnique()
            .HasFilter("external_invoice_id IS NOT NULL")
            .HasDatabaseName("ix_subscription_invoices_external_invoice_id");

        builder.HasIndex(i => new { i.TenantId, i.Status })
            .HasDatabaseName("ix_subscription_invoices_tenant_id_status");

        builder.HasIndex(i => new { i.TenantId, i.DueAt })
            .HasDatabaseName("ix_subscription_invoices_tenant_id_due_at");

        builder.HasOne<Tenant>()
            .WithMany()
            .HasForeignKey(i => i.TenantId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne<TenantSubscription>()
            .WithMany()
            .HasForeignKey(i => i.TenantSubscriptionId)
            .OnDelete(DeleteBehavior.SetNull);
    }
}
