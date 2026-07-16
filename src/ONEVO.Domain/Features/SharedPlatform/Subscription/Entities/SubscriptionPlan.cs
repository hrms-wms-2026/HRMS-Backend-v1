namespace ONEVO.Domain.Features.SharedPlatform.Entities;

public class SubscriptionPlan
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Code { get; set; } = string.Empty;
    public string Tier { get; set; } = string.Empty;
    public string? FeatureLimitsJson { get; set; }
    public string? IncludedModulesJson { get; set; }
    public string CompanySizeRange { get; set; } = string.Empty;
    public string PricingUnit { get; set; } = "per_employee";
    public decimal CalculatedMonthlyPrice { get; set; }
    public decimal CalculatedAnnualPrice { get; set; }
    public decimal? OverrideMonthlyPrice { get; set; }
    public decimal? OverrideAnnualPrice { get; set; }
    public int? AiTokenLimitPerMonth { get; set; }
    public string Currency { get; set; } = "USD";
    public int TrialPeriodDays { get; set; } = 30;
    public int UnpaidGracePeriodDays { get; set; } = 7;
    public bool IsActive { get; set; } = true;
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? UpdatedAt { get; set; }

    public IReadOnlyList<string> GetIncludedModules() =>
        string.IsNullOrWhiteSpace(IncludedModulesJson)
            ? []
            : System.Text.Json.JsonSerializer.Deserialize<List<string>>(IncludedModulesJson) ?? [];
}
