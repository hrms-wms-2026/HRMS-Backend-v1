namespace ONEVO.Application.Features.DevPlatform.Subscription.DTOs.Requests;

public record CreateSubscriptionPlanRequest(
    string Name,
    string Code,
    string Tier,
    string CompanySizeRange,
    IReadOnlyList<string> ModuleKeys,
    string Currency = "USD",
    decimal? OverrideMonthlyPrice = null,
    decimal? OverrideAnnualPrice = null,
    int? AiTokenLimitPerMonth = null,
    int TrialPeriodDays = 30,
    int UnpaidGracePeriodDays = 7);
