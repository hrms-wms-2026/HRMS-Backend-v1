namespace ONEVO.Application.Features.Storage.Quota.DTOs.Responses;

/// <summary>
/// A tenant's resolved storage allowance and where it came from.
/// </summary>
/// <param name="LimitBytes">Total allowed storage in bytes. Never negative.</param>
/// <param name="Source">
/// Which precedence rule produced the limit:
/// <c>subscription_modules</c> (storage contributed by the modules on the active
/// subscription) or <c>platform_default</c> (configured fallback when the tenant
/// has no subscription-derived storage).
/// </param>
public sealed record TenantStorageLimitDto(long LimitBytes, string Source);
