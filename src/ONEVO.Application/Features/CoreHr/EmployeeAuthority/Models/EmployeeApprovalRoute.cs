namespace ONEVO.Application.Features.CoreHr.EmployeeAuthority.Models;

/// <summary>Successful result of IEmployeeAuthorityResolver.ResolveApproverAsync.
/// OwnerOrder is the coverage record's owner level (1=Primary, 2=Backup1, ...) when Source is
/// PositionCoverage, DepartmentCoverage, or CompanyCoverage, and null when Source is
/// ReportingLine.</summary>
public sealed record EmployeeApprovalRoute(
    Guid ApproverEmployeeId,
    Guid ApproverUserId,
    Guid ApproverPositionId,
    string RequiredPermission,
    EmployeeAuthorityPurpose Purpose,
    EmployeeApprovalRouteSource Source,
    int? OwnerOrder);
