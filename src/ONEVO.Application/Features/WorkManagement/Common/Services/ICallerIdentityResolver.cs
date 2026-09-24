namespace ONEVO.Application.Features.WorkManagement.Common.Services;

/// <summary>Name plus the raw file id of an employee's uploaded avatar, if any - resolving that id to a
/// signed URL is the caller's job via IFileStorageService, not this resolver's (same coverage-free
/// identity data as the display name; a photo carries no more sensitivity than a name).</summary>
public sealed record EmployeeIdentityDto(string Name, Guid? AvatarFileId);

/// <summary>
/// Resolves the current session's UserId to the caller's Employee.Id within this tenant - the
/// single seam every Work Management handler goes through instead of comparing UserId directly
/// (see Phase 2 preamble, docs/superpowers/plans/next/2026-08-14-work-management-objective-member-management.md).
/// </summary>
public interface ICallerIdentityResolver
{
    /// <summary>Null if the caller has no active Employee record in this tenant.</summary>
    Task<Guid?> ResolveCallerEmployeeIdAsync(Guid tenantId, Guid userId, CancellationToken ct = default);

    /// <summary>Batch name lookup by Employee.Id (not UserId) - for resolving OwnerId/ReportingManagerId
    /// display names without a second round trip per id. Employees are looked up individually via the
    /// existing single-item IEmployeeRepository.GetByIdAsync in a loop rather than a new batch method on
    /// Core HR's interface, per this phase's scope guardrail (no Core HR file changes).</summary>
    Task<IReadOnlyDictionary<Guid, string>> ResolveDisplayNamesByEmployeeIdAsync(Guid tenantId, IReadOnlyList<Guid> employeeIds, CancellationToken ct = default);

    /// <summary>Same batch lookup as ResolveDisplayNamesByEmployeeIdAsync, but also carries each
    /// employee's AvatarFileId so a caller rendering avatars (not just names) doesn't need a second
    /// pass through Core HR. Kept as a separate method rather than widening the existing one so the
    /// 15+ existing name-only callers are untouched.</summary>
    Task<IReadOnlyDictionary<Guid, EmployeeIdentityDto>> ResolveIdentitiesByEmployeeIdAsync(Guid tenantId, IReadOnlyList<Guid> employeeIds, CancellationToken ct = default);
}
