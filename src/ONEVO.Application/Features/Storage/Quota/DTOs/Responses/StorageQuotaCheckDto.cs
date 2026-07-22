namespace ONEVO.Application.Features.Storage.Quota.DTOs.Responses;

/// <summary>
/// Result of asking whether a tenant may consume additional storage bytes.
/// </summary>
/// <param name="Allowed">True when the requested bytes fit within the limit.</param>
/// <param name="LimitBytes">The resolved total allowance.</param>
/// <param name="TotalUsedBytes">Current usage including reserved bytes.</param>
/// <param name="RequestedBytes">The bytes the caller asked to add.</param>
/// <param name="RemainingBytes">Bytes still available before the check. Never negative.</param>
/// <param name="DenialReason">A StorageQuotaErrorCodes value when denied; null when allowed.</param>
public sealed record StorageQuotaCheckDto(
    bool Allowed,
    long LimitBytes,
    long TotalUsedBytes,
    long RequestedBytes,
    long RemainingBytes,
    string? DenialReason);
