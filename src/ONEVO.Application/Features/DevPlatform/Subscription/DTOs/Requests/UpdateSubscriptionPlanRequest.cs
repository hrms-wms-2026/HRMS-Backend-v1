namespace ONEVO.Application.Features.DevPlatform.Subscription.DTOs.Requests;

public record UpdateSubscriptionPlanRequest(
    string? Name = null,
    string? Tier = null,
    string? CompanySizeRange = null,
    IReadOnlyList<string>? ModuleKeys = null,
    string? Currency = null,
    decimal? OverrideMonthlyPrice = null,
    decimal? OverrideAnnualPrice = null,
    int? AiTokenLimitPerMonth = null,
    int? TrialPeriodDays = null,
    int? UnpaidGracePeriodDays = null);
