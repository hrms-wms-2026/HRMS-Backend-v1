using MediatR;
using ONEVO.Application.Common.Authorization;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.CoreHr.Employee.Access;
using ONEVO.Application.Features.Monitoring.Reports.DTOs.Responses;
using ONEVO.Application.Features.Monitoring.Reports.RepositoryInterfaces;

namespace ONEVO.Application.Features.Monitoring.Reports.Queries.GetProductivityReport;

/// <summary>
/// Product decision 2026-11-23 ("coverage only"): a caller sees productivity only for the employees
/// they may view - themselves plus their management coverage, the IEmployeeViewAccess rule that
/// /employees/{id}/overview/activity uses. HR and admins keep the tenant-wide view through
/// org:manage, the same tenant-wide test EmployeeDocumentAccess and the employee detail, position
/// history and bank-detail handlers apply before falling back to coverage.
/// </summary>
public class GetProductivityReportQueryHandler
    : IRequestHandler<GetProductivityReportQuery, Result<ProductivityReportDto>>
{
    private const int MaxRangeDays = 366;

    private readonly IProductivityReportRepository _reports;
    private readonly ITenantContext _tenantContext;
    private readonly ICurrentUser _currentUser;
    private readonly IEmployeeViewAccess _viewAccess;

    public GetProductivityReportQueryHandler(
        IProductivityReportRepository reports,
        ITenantContext tenantContext,
        ICurrentUser currentUser,
        IEmployeeViewAccess viewAccess)
    {
        _reports = reports;
        _tenantContext = tenantContext;
        _currentUser = currentUser;
        _viewAccess = viewAccess;
    }

    public async Task<Result<ProductivityReportDto>> Handle(GetProductivityReportQuery request, CancellationToken ct)
    {
        if (_tenantContext.TenantId == Guid.Empty)
            return Result<ProductivityReportDto>.Failure("Tenant context is required.", 401);

        if (request.From > request.To)
            return Result<ProductivityReportDto>.Failure("from must not be after to.", 400);

        if (request.To.DayNumber - request.From.DayNumber > MaxRangeDays)
            return Result<ProductivityReportDto>.Failure($"Date range cannot exceed {MaxRangeDays} days.", 400);

        if (request.Scope != ProductivityReportScope.Tenant && request.ScopeId is null)
            return Result<ProductivityReportDto>.Failure("scopeId is required for employee/department scope.", 400);

        var tenantId = _tenantContext.TenantId;

        // Null: tenant-wide. Otherwise the only employees whose minutes may be summed.
        IReadOnlyCollection<Guid>? viewable = null;
        if (!_currentUser.HasPermission(PermissionCatalog.Org.Manage))
        {
            if (request.Scope == ProductivityReportScope.Employee)
            {
                if (!await _viewAccess.CanViewAsync(request.ScopeId!.Value, ct))
                    return Result<ProductivityReportDto>.Failure("You do not have access to this employee.", 403);
            }
            else
            {
                viewable = await _viewAccess.ListViewableEmployeeIdsAsync(ct);
            }
        }

        ProductivityAggregate? aggregate = (request.Scope, viewable) switch
        {
            (ProductivityReportScope.Employee, _) =>
                await _reports.GetEmployeeAggregateAsync(tenantId, request.ScopeId!.Value, request.From, request.To, ct),
            (ProductivityReportScope.Department, null) =>
                await _reports.GetDepartmentAggregateAsync(tenantId, request.ScopeId!.Value, request.From, request.To, ct),
            (ProductivityReportScope.Department, { } departmentIds) =>
                await _reports.GetDepartmentAggregateForEmployeesAsync(
                    tenantId, request.ScopeId!.Value, departmentIds, request.From, request.To, ct),
            (_, null) => await _reports.GetTenantAggregateAsync(tenantId, request.From, request.To, ct),
            (_, { } tenantIds) => await _reports.GetEmployeesAggregateAsync(tenantId, tenantIds, request.From, request.To, ct)
        };

        if (aggregate is null)
            return Result<ProductivityReportDto>.NotFound("Department not found.");

        return Result<ProductivityReportDto>.Success(new ProductivityReportDto(
            aggregate.TotalActiveMinutes, aggregate.TotalIdleMinutes, aggregate.TotalMeetingMinutes,
            aggregate.ProductiveAppMinutes, aggregate.PersonalAppMinutes, aggregate.UnknownAppMinutes,
            aggregate.AverageActivityScore, aggregate.TotalWorkedMinutes, aggregate.TotalBreakMinutes,
            aggregate.DayCount));
    }
}
