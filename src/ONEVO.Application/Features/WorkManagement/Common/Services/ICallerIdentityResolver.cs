namespace ONEVO.Application.Features.WorkManagement.Common.Services;

/// <summary>
/// Resolves the current session's UserId to the caller's Employee.Id within this tenant - the
/// single seam every Work Management handler goes through instead of comparing UserId directly
/// (see Phase 2 preamble, docs/superpowers/plans/next/2026-08-14-work-management-objective-member-management.md).
/// </summary>
public interface ICallerIdentityResolver
{
    /// <summary>Null if the caller has no active Employee record in this tenant.</summary>
    Task<Guid?> ResolveCallerEmployeeIdAsync(Guid tenantId, Guid userId, CancellationToken ct = default);
}
