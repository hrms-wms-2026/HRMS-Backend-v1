using MediatR;
using Microsoft.Extensions.Options;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.Calendar.RepositoryInterfaces;
using ONEVO.Application.Features.CoreHr.Employee.RepositoryInterfaces;
using ONEVO.Application.Features.Leave.Request.RepositoryInterfaces;
using ONEVO.Application.Features.OrgStructure.RepositoryInterfaces;
using ONEVO.Application.Features.WorkManagement.Leadership.DTOs;
using ONEVO.Application.Features.WorkManagement.Leadership.Services;
using ONEVO.Application.Features.WorkManagement.Planner;
using ONEVO.Application.Features.WorkManagement.Planner.RepositoryInterfaces;

namespace ONEVO.Application.Features.WorkManagement.Leadership.Queries.GetLedTimeProgress;

/// <summary>Same gate and scope as GetLedPeriodProgressQueryHandler: the window comes from today's UTC date, the filters
/// only narrow the led scope, and a caller who leads nothing (or a filter outside the scope) gets an empty response with
/// From/To filled in. Loading is shared with led-progress/time/unscheduled (<see cref="LedTimeInputsLoader"/>).</summary>
public sealed class GetLedTimeProgressQueryHandler(
    ICurrentUser currentUser,
    IDateTimeProvider clock,
    IEmployeeRepository employees,
    IWorkLeadershipService leadership,
    ITeamTimeReadRepository teamTime,
    ILegalEntityRepository legalEntities,
    ICalendarEventRepository calendarEvents,
    ILeaveRequestReadRepository leaveRequests,
    IOptions<PlannerOptions> plannerOptions)
    : IRequestHandler<GetLedTimeProgressQuery, Result<LedTimeProgressResponse>>
{
    public async Task<Result<LedTimeProgressResponse>> Handle(GetLedTimeProgressQuery request, CancellationToken ct)
    {
        if (!currentUser.IsAuthenticated || currentUser.TenantId == Guid.Empty)
            return Result<LedTimeProgressResponse>.Forbidden("Authentication required.");

        var loader = new LedTimeInputsLoader(clock, employees, leadership, teamTime, legalEntities, calendarEvents, leaveRequests,
            plannerOptions.Value);
        var i = await loader.LoadAsync(currentUser.TenantId, currentUser.UserId, request.Period, request.ProjectId, request.ObjectiveId,
            includeExpected: true, ct);

        return Result<LedTimeProgressResponse>.Success(
            LedTimeProgressCalculator.Build(i.Period, i.Window.From, i.Window.To, i.Today, i.Now, i.PlannerEnabled, i.People, i.Tasks));
    }
}
