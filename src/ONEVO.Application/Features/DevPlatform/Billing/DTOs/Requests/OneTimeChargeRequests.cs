namespace ONEVO.Application.Features.DevPlatform.Billing.DTOs.Requests;

public sealed record CreateOneTimeChargeRequest(
    string SetupOptionKey,
    string Description,
    decimal Amount,
    string Currency = "USD");

public sealed record UpdateOneTimeChargeRequest(
    decimal? Amount,
    string? Status);
