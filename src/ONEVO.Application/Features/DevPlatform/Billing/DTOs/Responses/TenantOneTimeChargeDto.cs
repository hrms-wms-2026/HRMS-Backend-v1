namespace ONEVO.Application.Features.DevPlatform.Billing.DTOs.Responses;

public sealed record TenantOneTimeChargeDto(
    Guid Id,
    Guid TenantId,
    string SetupOptionKey,
    string Description,
    decimal Amount,
    string Currency,
    string Status,
    bool ChargedOnce,
    DateTimeOffset CreatedAt);
