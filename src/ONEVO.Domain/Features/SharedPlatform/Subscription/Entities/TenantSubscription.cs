using ONEVO.Domain.Common;

namespace ONEVO.Domain.Features.SharedPlatform.Entities;

public class TenantSubscription : ITenantOwnedEntity
{
    public Guid Id { get; set; }
    public Guid TenantId { get; set; }
    public Guid PlanId { get; set; }
    public string BillingCycle { get; set; } = "monthly";
    public string Status { get; set; } = "trialing";
    public DateOnly CurrentPeriodStart { get; set; }
    public DateOnly CurrentPeriodEnd { get; set; }
    public string? PaymentProviderRef { get; set; }
    public string CommercialModel { get; set; } = "subscription";
    public string BillingCurrency { get; set; } = "USD";
    public string? SubscriptionCollectionMode { get; set; }
    public string? GatewayProvider { get; set; }
    public string? GatewayCustomerRef { get; set; }
    public string? GatewaySubscriptionRef { get; set; }
    public string? LicensePaymentMode { get; set; }
    public decimal? FullLicenseAmount { get; set; }
    public DateOnly? LicensePaidAt { get; set; }
    public string? LicenseReference { get; set; }
    public string? MaintenanceCollectionMode { get; set; }
    public string? MaintenanceBillingCycle { get; set; }
    public DateOnly ContractStartDate { get; set; }
    public DateOnly? ContractEndDate { get; set; }
    public string? MaintenanceStatus { get; set; }
    public DateOnly? MaintenanceStartDate { get; set; }
    public DateOnly? MaintenanceRenewalDate { get; set; }
    public decimal? MaintenanceRate { get; set; }
    public decimal? MaintenanceAmount { get; set; }
    public string CompanySizeRange { get; set; } = "51-200";
    public string SelectedModulesJson { get; set; } = "[]";
    public decimal CalculatedMonthlyPrice { get; set; }
    public decimal CalculatedAnnualPrice { get; set; }
    public decimal? OverrideMonthlyPrice { get; set; }
    public decimal? OverrideAnnualPrice { get; set; }
    public int? AiTokenLimitPerMonth { get; set; }
    public decimal? CustomContractValue { get; set; }
    public decimal? DiscountPercent { get; set; }
    public DateTimeOffset? TrialStartDate { get; set; }
    public DateTimeOffset? TrialEndDate { get; set; }
    public int UnpaidGracePeriodDays { get; set; } = 7;
    public DateTimeOffset? AccessEndsAt { get; set; }
    public Guid? CreatedById { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? UpdatedAt { get; set; }
}
