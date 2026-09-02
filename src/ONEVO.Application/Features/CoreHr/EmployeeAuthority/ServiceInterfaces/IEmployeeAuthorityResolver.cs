using ONEVO.Application.Common.Models;
using ONEVO.Application.Features.CoreHr.EmployeeAuthority.Models;

namespace ONEVO.Application.Features.CoreHr.EmployeeAuthority.ServiceInterfaces;

/// <summary>
/// Generic, purpose-agnostic backend authority foundation answering two questions for any
/// employee-scoped feature: which employees can the current actor see/manage (visibility), and
/// who is the correct approver for a request about a subject employee (approval routing). Backed
/// by management_coverage_records and the position/department reporting hierarchy. Both methods
/// derive tenant context from the injected ICurrentUser - callers never pass a TenantId.
/// </summary>
public interface IEmployeeAuthorityResolver
{
    Task<EmployeeAuthorityVisibilityScope> ResolveVisibilityAsync(
        EmployeeAuthorityVisibilityRequest request,
        CancellationToken cancellationToken = default);

    Task<Result<EmployeeApprovalRoute>> ResolveApproverAsync(
        EmployeeApprovalRouteRequest request,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Narrows a bounded candidate set to exactly the employees for whom the authenticated
    /// reviewer (ICurrentUser.UserId) is the current exact approver - identical per-candidate
    /// results to calling ResolveApproverAsync for each candidate and keeping only those whose
    /// ApproverUserId equals the reviewer. Reviewer and tenant identity are both server-derived
    /// from ICurrentUser; there is no reviewer-identity parameter on the request. Fails closed
    /// (returns an empty collection) when the caller is unauthenticated, has no active employee
    /// record in the requested legal entity, or lacks RequiredPermission.
    /// </summary>
    Task<IReadOnlyCollection<Guid>> ResolveApprovalInboxScopeAsync(
        EmployeeApprovalInboxScopeRequest request,
        CancellationToken cancellationToken = default);
}
