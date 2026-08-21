using MediatR;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.Monitoring.Reports.DTOs.Responses;
using ONEVO.Application.Features.Monitoring.Reports.RepositoryInterfaces;

namespace ONEVO.Application.Features.Monitoring.Reports.Queries.GetProductivityReport;

public class GetProductivityReportQueryHandler
    : IRequestHandler<GetProductivityReportQuery, Result<ProductivityReportDto>>
{
    private const int MaxRangeDays = 366;

    private readonly IProductivityReportRepository _reports;
    private readonly ITenantContext _tenantContext;

    public GetProductivityReportQueryHandler(IProductivityReportRepository reports, ITenantContext tenantContext)
    {
        _reports = reports;
        _tenantContext = tenantContext;
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

        ProductivityAggregate? aggregate = request.Scope switch
        {
            ProductivityReportScope.Employee =>
                await _reports.GetEmployeeAggregateAsync(tenantId, request.ScopeId!.Value, request.From, request.To, ct),
            ProductivityReportScope.Department =>
                await _reports.GetDepartmentAggregateAsync(tenantId, request.ScopeId!.Value, request.From, request.To, ct),
            _ => await _reports.GetTenantAggregateAsync(tenantId, request.From, request.To, ct)
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
