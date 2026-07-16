using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using ONEVO.Domain.Features.SharedPlatform.Entities;

namespace ONEVO.Infrastructure.Persistence.Configurations.DevPlatform.Subscription;

public class TenantSubscriptionConfiguration : IEntityTypeConfiguration<TenantSubscription>
{
    public void Configure(EntityTypeBuilder<TenantSubscription> builder)
    {
        builder.ToTable("tenant_subscriptions");
        builder.HasKey(t => t.Id);

        builder.Property(t => t.TenantId).HasColumnName("tenant_id").IsRequired();
        builder.Property(t => t.PlanId).HasColumnName("plan_id").IsRequired();
        builder.Property(t => t.BillingCycle).HasColumnName("billing_cycle").HasMaxLength(20).IsRequired();
        builder.Property(t => t.Status).HasMaxLength(20).IsRequired();
        builder.Property(t => t.CurrentPeriodStart).HasColumnName("current_period_start").IsRequired();
        builder.Property(t => t.CurrentPeriodEnd).HasColumnName("current_period_end").IsRequired();
        builder.Property(t => t.PaymentProviderRef).HasColumnName("payment_provider_ref").HasMaxLength(200);
        builder.Property(t => t.CommercialModel).HasColumnName("commercial_model").HasMaxLength(30).IsRequired();
        builder.Property(t => t.BillingCurrency).HasColumnName("billing_currency").HasMaxLength(3).IsRequired();
        builder.Property(t => t.SubscriptionCollectionMode).HasColumnName("subscription_collection_mode").HasMaxLength(30);
        builder.Property(t => t.GatewayProvider).HasColumnName("gateway_provider").HasMaxLength(50);
        builder.Property(t => t.GatewayCustomerRef).HasColumnName("gateway_customer_ref").HasMaxLength(200);
        builder.Property(t => t.GatewaySubscriptionRef).HasColumnName("gateway_subscription_ref").HasMaxLength(200);
        builder.Property(t => t.LicensePaymentMode).HasColumnName("license_payment_mode").HasMaxLength(30);
        builder.Property(t => t.FullLicenseAmount).HasColumnName("full_license_amount").HasColumnType("decimal(14,2)");
        builder.Property(t => t.LicensePaidAt).HasColumnName("license_paid_at");
        builder.Property(t => t.LicenseReference).HasColumnName("license_reference").HasMaxLength(100);
        builder.Property(t => t.MaintenanceCollectionMode).HasColumnName("maintenance_collection_mode").HasMaxLength(30);
        builder.Property(t => t.MaintenanceBillingCycle).HasColumnName("maintenance_billing_cycle").HasMaxLength(20);
        builder.Property(t => t.ContractStartDate).HasColumnName("contract_start_date").IsRequired();
        builder.Property(t => t.ContractEndDate).HasColumnName("contract_end_date");
        builder.Property(t => t.MaintenanceStatus).HasColumnName("maintenance_status").HasMaxLength(20);
        builder.Property(t => t.MaintenanceStartDate).HasColumnName("maintenance_start_date");
        builder.Property(t => t.MaintenanceRenewalDate).HasColumnName("maintenance_renewal_date");
        builder.Property(t => t.MaintenanceRate).HasColumnName("maintenance_rate").HasColumnType("decimal(5,2)");
        builder.Property(t => t.MaintenanceAmount).HasColumnName("maintenance_amount").HasColumnType("decimal(14,2)");
        builder.Property(t => t.CompanySizeRange).HasColumnName("company_size_range").HasMaxLength(30).IsRequired();
        builder.Property(t => t.SelectedModulesJson).HasColumnName("selected_modules").HasColumnType("jsonb").IsRequired();
        builder.Property(t => t.CalculatedMonthlyPrice).HasColumnName("calculated_monthly_price").HasColumnType("decimal(10,2)");
        builder.Property(t => t.CalculatedAnnualPrice).HasColumnName("calculated_annual_price").HasColumnType("decimal(10,2)");
        builder.Property(t => t.OverrideMonthlyPrice).HasColumnName("override_monthly_price").HasColumnType("decimal(10,2)");
        builder.Property(t => t.OverrideAnnualPrice).HasColumnName("override_annual_price").HasColumnType("decimal(10,2)");
        builder.Property(t => t.AiTokenLimitPerMonth).HasColumnName("ai_token_limit_per_month");
        builder.Property(t => t.CustomContractValue).HasColumnName("custom_contract_value").HasColumnType("decimal(14,2)");
        builder.Property(t => t.DiscountPercent).HasColumnName("discount_percent").HasColumnType("decimal(5,2)");
        builder.Property(t => t.TrialStartDate).HasColumnName("trial_start_date");
        builder.Property(t => t.TrialEndDate).HasColumnName("trial_end_date");
        builder.Property(t => t.UnpaidGracePeriodDays).HasColumnName("unpaid_grace_period_days");
        builder.Property(t => t.AccessEndsAt).HasColumnName("access_ends_at");
        builder.Property(t => t.CreatedById).HasColumnName("created_by_id");

        builder.HasIndex(t => t.TenantId);
    }
}
