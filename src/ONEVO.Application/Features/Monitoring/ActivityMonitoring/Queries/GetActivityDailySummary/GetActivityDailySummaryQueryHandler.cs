using System.Text.Json;
using MediatR;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.Monitoring.ActivityMonitoring.DTOs.Responses;
using ONEVO.Application.Features.Monitoring.ActivityMonitoring.RepositoryInterfaces;
using ONEVO.Domain.Features.Monitoring.ActivityMonitoring.Entities;

namespace ONEVO.Application.Features.Monitoring.ActivityMonitoring.Queries.GetActivityDailySummary;

public class GetActivityDailySummaryQueryHandler
    : IRequestHandler<GetActivityDailySummaryQuery, Result<ActivityDailySummaryDto?>>
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    private readonly IActivityDailySummaryRepository _summaries;
    private readonly ITenantContext _tenantContext;

    public GetActivityDailySummaryQueryHandler(
        IActivityDailySummaryRepository summaries,
        ITenantContext tenantContext)
    {
        _summaries = summaries;
        _tenantContext = tenantContext;
    }

    public async Task<Result<ActivityDailySummaryDto?>> Handle(
        GetActivityDailySummaryQuery request,
        CancellationToken cancellationToken)
    {
        if (_tenantContext.TenantId == Guid.Empty)
            return Result<ActivityDailySummaryDto?>.Failure("Tenant context is required.", 401);

        if (request.EmployeeId == Guid.Empty)
            return Result<ActivityDailySummaryDto?>.Failure("employeeId is required.", 400);

        var entity = await _summaries.GetAsync(
            _tenantContext.TenantId,
            request.EmployeeId,
            request.Date,
            cancellationToken);

        return Result<ActivityDailySummaryDto?>.Success(
            entity is null ? null : Map(entity));
    }

    internal static ActivityDailySummaryDto Map(ActivityDailySummary entity)
    {
        List<AppUsageSummary> topApps = [];
        if (!string.IsNullOrWhiteSpace(entity.TopAppsJson) && entity.TopAppsJson != "[]")
        {
            try
            {
                topApps = JsonSerializer.Deserialize<List<AppUsageSummary>>(entity.TopAppsJson, JsonOptions)
                          ?? [];
            }
            catch (JsonException)
            {
                topApps = [];
            }
        }

        return new ActivityDailySummaryDto
        {
            EmployeeId = entity.EmployeeId,
            Date = entity.Date,
            TotalActiveMinutes = entity.TotalActiveMinutes,
            TotalIdleMinutes = entity.TotalIdleMinutes,
            TotalMeetingMinutes = entity.TotalMeetingMinutes,
            ActivePercentage = entity.ActivePercentage,
            ActivityScore = entity.ActivityScore,
            KeyboardTotal = entity.KeyboardTotal,
            MouseTotal = entity.MouseTotal,
            FocusMinutes = entity.FocusMinutes,
            DeepFocusSessionsCount = entity.DeepFocusSessionsCount,
            IntensityAvg = entity.IntensityAvg,
            DataCoveragePercentage = entity.DataCoveragePercentage,
            TopApps = topApps
        };
    }
}
