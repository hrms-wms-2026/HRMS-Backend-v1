namespace ONEVO.Application.Features.CoreHr.EmployeeAuthority.Models;

/// <summary>
/// Input to IEmployeeAuthorityResolver.ResolveVisibilityAsync. Deliberately has no TenantId -
/// the resolver derives tenant context itself from ICurrentUser and never accepts it from a
/// caller, matching every other Application handler in this codebase.
/// </summary>
public sealed record EmployeeAuthorityVisibilityRequest(
    Guid ActorUserId,
    Guid LegalEntityId,
    string RequiredPermission,
    bool IncludeSelf,
    EmployeeAuthorityPurpose Purpose);
