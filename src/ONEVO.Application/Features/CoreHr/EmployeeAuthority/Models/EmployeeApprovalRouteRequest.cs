namespace ONEVO.Application.Features.CoreHr.EmployeeAuthority.Models;

/// <summary>
/// Input to IEmployeeAuthorityResolver.ResolveApproverAsync. No TenantId - derived from
/// ICurrentUser by the resolver, same rationale as EmployeeAuthorityVisibilityRequest.
/// </summary>
public sealed record EmployeeApprovalRouteRequest(
    Guid SubjectEmployeeId,
    Guid LegalEntityId,
    string RequiredPermission,
    EmployeeAuthorityPurpose Purpose);
