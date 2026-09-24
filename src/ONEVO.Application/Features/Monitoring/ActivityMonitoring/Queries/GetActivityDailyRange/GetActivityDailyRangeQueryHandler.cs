using MediatR;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.Monitoring.ActivityMonitoring.DTOs.Responses;
using ONEVO.Application.Features.Monitoring.ActivityMonitoring.Queries.GetActivityDailySummary;
using ONEVO.Application.Features.Monitoring.ActivityMonitoring.RepositoryInterfaces;

namespace ONEVO.Application.Features.Monitoring.ActivityMonitoring.Queries.GetActivityDailyRange;

public class GetActivityDailyRangeQueryHandler
    : IRequestHandler<GetActivityDailyRangeQuery, Result<List<ActivityDailySummaryDto>>>
{
    private const int MaxRangeDays = 31;

    private readonly IActivityDailySummaryRepository _summaries;
    private readonly ITenantContext _tenantContext;

    public GetActivityDailyRangeQueryHandler(
        IActivityDailySummaryRepository summaries,
        ITenantContext tenantContext)
    {
        _summaries = summaries;
        _tenantContext = tenantContext;
    }

    public async Task<Result<List<ActivityDailySummaryDto>>> Handle(
        GetActivityDailyRangeQuery request,
        CancellationToken cancellationToken)
    {
        if (_tenantContext.TenantId == Guid.Empty)
            return Result<List<ActivityDailySummaryDto>>.Failure("Tenant context is required.", 401);

        if (request.EmployeeId == Guid.Empty)
            return Result<List<ActivityDailySummaryDto>>.Failure("employeeId is required.", 400);

        if (request.To < request.From)
            return Result<List<ActivityDailySummaryDto>>.Failure("'to' must be on or after 'from'.", 400);

        var daySpan = request.To.DayNumber - request.From.DayNumber + 1;
        if (daySpan > MaxRangeDays)
            return Result<List<ActivityDailySummaryDto>>.Failure(
                $"Date range cannot exceed {MaxRangeDays} days.", 400);

        var entities = await _summaries.GetRangeAsync(
            _tenantContext.TenantId,
            request.EmployeeId,
            request.From,
            request.To,
            cancellationToken);

        var dtos = entities
            .Select(GetActivityDailySummaryQueryHandler.Map)
            .ToList();

        return Result<List<ActivityDailySummaryDto>>.Success(dtos);
    }
}
