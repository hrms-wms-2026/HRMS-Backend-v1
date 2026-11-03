using MediatR;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.CoreHr.Employee.RepositoryInterfaces;
using ONEVO.Application.Features.WorkManagement.Leadership.DTOs;
using ONEVO.Application.Features.WorkManagement.Leadership.RepositoryInterfaces;
using ONEVO.Application.Features.WorkManagement.Leadership.Services;

namespace ONEVO.Application.Features.WorkManagement.Leadership.Queries.GetLedPeriodProgress;

/// <summary>Same gate and scope as GetLedWorkProgressQueryHandler. The window is computed from
/// today's UTC date (LedProgressPeriod). Filters only ever narrow the led scope; a filter outside
/// it, or a caller who leads nothing, gets zero totals - From/To are still filled in.</summary>
public sealed class GetLedPeriodProgressQueryHandler(
    ICurrentUser currentUser,
    IDateTimeProvider clock,
    IEmployeeRepository employees,
    IWorkLeadershipService leadership,
    ILedProgressReadRepository reads)
    : IRequestHandler<GetLedPeriodProgressQuery, Result<LedPeriodProgressResponse>>
{
    public async Task<Result<LedPeriodProgressResponse>> Handle(GetLedPeriodProgressQuery request, CancellationToken ct)
    {
        if (!currentUser.IsAuthenticated || currentUser.TenantId == Guid.Empty)
            return Result<LedPeriodProgressResponse>.Forbidden("Authentication required.");

        // The validator only lets week|month through; Parse never yields All here, but fall back to
        // the week rather than an unbounded window if it ever did.
        var period = LedProgressQueryOptions.Parse(request.Period, LedProgressPeriod.WindowedPeriods, LedPeriod.Week);
        var window = LedProgressPeriod.Window(period, LedProgressPeriod.UtcToday(clock.UtcNow))!;
        LedPeriodProgressResponse Build(LedPeriodTotalsRow t) => new(
            LedProgressPeriod.Wire(period), window.From, window.To,
            t.CompletedHours, t.InProgressHours, t.NotStartedHours, t.TotalHours,
            t.TaskCount, t.CompletedTaskCount, t.InProgressTaskCount, t.NotStartedTaskCount, t.UnestimatedTaskCount);

        var tenantId = currentUser.TenantId;
        var caller = await employees.GetDefaultForUserAsync(tenantId, currentUser.UserId, ct);
        if (caller?.LegalEntityId is not Guid legalEntityId)
            return Result<LedPeriodProgressResponse>.Success(Build(LedPeriodTotalsRow.Zero));

        var scope = await leadership.ResolveLedScopeAsync(tenantId, caller.Id, legalEntityId, ct);
        var objectiveIds = LedScopeFilter.Resolve(scope, request.ProjectId, request.ObjectiveId);
        if (objectiveIds.Count == 0)
            return Result<LedPeriodProgressResponse>.Success(Build(LedPeriodTotalsRow.Zero));

        var totals = await reads.GetPeriodTotalsAsync(tenantId, objectiveIds, window, ct);
        return Result<LedPeriodProgressResponse>.Success(Build(totals));
    }
}
