namespace ONEVO.Application.Features.CoreHr.EmployeeAuthority.Models;

/// <summary>
/// Describes a bounded candidate set for an approval inbox. Tenant and reviewer identity are
/// both derived from ICurrentUser by the resolver - callers never pass either. The result
/// contains only candidates for which the authenticated reviewer is the current exact approver,
/// not merely a visible employee.
/// </summary>
public sealed record EmployeeApprovalInboxScopeRequest(
    Guid LegalEntityId,
    string RequiredPermission,
    EmployeeAuthorityPurpose Purpose,
    IReadOnlyCollection<Guid> CandidateEmployeeIds);
