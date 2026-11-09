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

namespace ONEVO.Application.Features.WorkManagement.Leadership.Queries.GetLedUnscheduledWork;

/// <summary>Same gate, window and scope as GetLedTimeProgressQueryHandler (shared <see cref="LedTimeInputsLoader"/>). The rows
/// come from each person's attributed time, so they are computed for the whole window and then paged; the kind totals cover
/// every row, not just the page. A caller who leads nothing, a filter outside the scope or the Planner off gets an empty page.</summary>
public sealed class GetLedUnscheduledWorkQueryHandler(
    ICurrentUser currentUser,
    IDateTimeProvider clock,
    IEmployeeRepository employees,
    IWorkLeadershipService leadership,
    ITeamTimeReadRepository teamTime,
    ILegalEntityRepository legalEntities,
    ICalendarEventRepository calendarEvents,
    ILeaveRequestReadRepository leaveRequests,
    IOptions<PlannerOptions> plannerOptions)
    : IRequestHandler<GetLedUnscheduledWorkQuery, Result<LedUnscheduledPage>>
{
    public async Task<Result<LedUnscheduledPage>> Handle(GetLedUnscheduledWorkQuery request, CancellationToken ct)
    {
        if (!currentUser.IsAuthenticated || currentUser.TenantId == Guid.Empty)
            return Result<LedUnscheduledPage>.Forbidden("Authentication required.");

        var (pageNumber, pageSize) = LedProgressQueryOptions.ClampPaging(request.PageNumber, request.PageSize);
        var kind = LedProgressQueryOptions.Parse(request.Kind, GetLedUnscheduledWorkQueryValidator.Kinds, "all");

        var loader = new LedTimeInputsLoader(clock, employees, leadership, teamTime, legalEntities, calendarEvents, leaveRequests,
            plannerOptions.Value);
        var i = await loader.LoadAsync(currentUser.TenantId, currentUser.UserId, request.Period, request.ProjectId, request.ObjectiveId,
            includeExpected: false, ct);

        var rows = LedTimeProgressCalculator.UnscheduledRows(i.Window.From, i.Window.To, i.Now, i.PlannerEnabled, i.People, i.Tasks);
        static LedUnscheduledKindTotal Total(IEnumerable<LedUnscheduledRow> list)
        {
            var items = list.ToList();
            return new LedUnscheduledKindTotal(items.Count, items.Sum(r => r.Minutes));
        }

        var selected = kind == "all" ? rows : rows.Where(r => r.Kind == kind).ToList();
        var page = selected.Skip(LedProgressQueryOptions.Skip(pageNumber, pageSize)).Take(pageSize).ToList();

        return Result<LedUnscheduledPage>.Success(new LedUnscheduledPage(
            page, pageNumber, pageSize, selected.Count,
            LedProgressPeriod.Wire(i.Period), i.Window.From, i.Window.To, i.PlannerEnabled, kind,
            Total(rows),
            Total(rows.Where(r => r.Kind == LedTimeProgressCalculator.NoPlanKind)),
            Total(rows.Where(r => r.Kind == LedTimeProgressCalculator.OutsideSlotKind))));
    }
}
