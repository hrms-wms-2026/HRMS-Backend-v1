using MediatR;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.CoreHr.Employee.RepositoryInterfaces;
using ONEVO.Application.Features.WorkManagement.Leadership.DTOs;
using ONEVO.Application.Features.WorkManagement.Leadership.RepositoryInterfaces;
using ONEVO.Application.Features.WorkManagement.Leadership.Services;
using ONEVO.Application.Features.WorkManagement.Projects.RepositoryInterfaces;

namespace ONEVO.Application.Features.WorkManagement.Leadership.Queries.GetLedWeeklyCompletion;

/// <summary>Same gate and scope as GetLedWorkProgressQueryHandler / GetLedPeriodProgressQueryHandler.
/// The weeks come from today's UTC date (LedCompletionWeeks). Filters only ever narrow the led
/// scope; a filter outside it, or a caller who leads nothing, gets zero-filled weeks and 0
/// allocation. AllocatedHours is totalled like led-progress (LedScopeFilter.AllocatedHours).</summary>
public sealed class GetLedWeeklyCompletionQueryHandler(
    ICurrentUser currentUser,
    IDateTimeProvider clock,
    IEmployeeRepository employees,
    IWorkLeadershipService leadership,
    IProjectRepository projects,
    ILedProgressReadRepository reads)
    : IRequestHandler<GetLedWeeklyCompletionQuery, Result<LedWeeklyCompletionResponse>>
{
    public async Task<Result<LedWeeklyCompletionResponse>> Handle(GetLedWeeklyCompletionQuery request, CancellationToken ct)
    {
        if (!currentUser.IsAuthenticated || currentUser.TenantId == Guid.Empty)
            return Result<LedWeeklyCompletionResponse>.Forbidden("Authentication required.");

        // The validator rejects anything outside 1..26; Windows clamps as a second line of defence.
        var weeks = LedCompletionWeeks.Windows(LedProgressPeriod.UtcToday(clock.UtcNow), request.Weeks);
        var zero = new LedWeeklyCompletionResponse(LedCompletionWeeks.Bucket(weeks, []), 0, 0, 0);

        var tenantId = currentUser.TenantId;
        var caller = await employees.GetDefaultForUserAsync(tenantId, currentUser.UserId, ct);
        if (caller?.LegalEntityId is not Guid legalEntityId)
            return Result<LedWeeklyCompletionResponse>.Success(zero);

        var scope = await leadership.ResolveLedScopeAsync(tenantId, caller.Id, legalEntityId, ct);
        var objectiveIds = LedScopeFilter.Resolve(scope, request.ProjectId, request.ObjectiveId);
        if (objectiveIds.Count == 0)
            return Result<LedWeeklyCompletionResponse>.Success(zero);

        // led-progress drops heads whose project the projects lookup no longer returns; do the same
        // so the unfiltered allocation is exactly hours.allocatedHours.
        var liveProjectIds = (await projects.ListByIdsAsync(
                tenantId, scope.HeadModules.Select(head => head.ProjectId).Distinct().ToList(), ct))
            .Select(project => project.Id)
            .ToHashSet();
        var allocated = LedScopeFilter.AllocatedHours(scope, liveProjectIds, request.ProjectId, request.ObjectiveId);

        var rows = await reads.GetCompletionsByDayAsync(tenantId, objectiveIds, LedCompletionWeeks.Span(weeks), ct);
        return Result<LedWeeklyCompletionResponse>.Success(new LedWeeklyCompletionResponse(
            LedCompletionWeeks.Bucket(weeks, rows.Days), allocated, rows.UndatedHours, rows.UndatedTaskCount));
    }
}
