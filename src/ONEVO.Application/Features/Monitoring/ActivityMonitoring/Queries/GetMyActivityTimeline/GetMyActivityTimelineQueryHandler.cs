using MediatR;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.CoreHr.Employee.RepositoryInterfaces;
using ONEVO.Application.Features.Monitoring.ActivityMonitoring.DTOs.Responses;
using ONEVO.Application.Features.Monitoring.ActivityMonitoring.RepositoryInterfaces;
using ONEVO.Application.Features.Monitoring.ActivityMonitoring.Services;

namespace ONEVO.Application.Features.Monitoring.ActivityMonitoring.Queries.GetMyActivityTimeline;

public sealed class GetMyActivityTimelineQueryHandler(
    ICurrentUser currentUser,
    IDateTimeProvider dateTime,
    IEmployeeRepository employees,
    IActivitySnapshotRepository snapshots)
    : IRequestHandler<GetMyActivityTimelineQuery, Result<ActivityTimelineDto>>
{
    public async Task<Result<ActivityTimelineDto>> Handle(
        GetMyActivityTimelineQuery request, CancellationToken ct)
    {
        if (!currentUser.IsAuthenticated)
            return Result<ActivityTimelineDto>.Forbidden();

        if (currentUser.TenantId == Guid.Empty)
            return Result<ActivityTimelineDto>.Forbidden("Tenant context missing.");

        var employee = await employees.GetDefaultForUserAsync(currentUser.TenantId, currentUser.UserId, ct);
        if (employee is null)
            return Result<ActivityTimelineDto>.NotFound("Current employee record was not found.");

        var date = request.Date ?? DateOnly.FromDateTime(dateTime.UtcNow.UtcDateTime);

        var snapshotsForDay = await snapshots.GetAllByEmployeeDateAsync(
            currentUser.TenantId, employee.Id, date, ct);

        var segments = ActivityTimelineBuilder.BuildSegments(snapshotsForDay);

        return Result<ActivityTimelineDto>.Success(new ActivityTimelineDto(date, segments));
    }
}
