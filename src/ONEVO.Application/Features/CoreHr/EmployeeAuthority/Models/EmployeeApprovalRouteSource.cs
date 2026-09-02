namespace ONEVO.Application.Features.CoreHr.EmployeeAuthority.Models;

/// <summary>Which rule resolved the approver, per EmployeeAuthorityResolver's fixed priority
/// order: PositionCoverage before DepartmentCoverage before ReportingLine before CompanyCoverage.</summary>
public enum EmployeeApprovalRouteSource
{
    PositionCoverage,
    DepartmentCoverage,
    ReportingLine,
    CompanyCoverage,
}
