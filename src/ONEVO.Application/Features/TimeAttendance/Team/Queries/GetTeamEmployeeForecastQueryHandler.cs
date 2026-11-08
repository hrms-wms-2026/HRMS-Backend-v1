using MediatR;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.CoreHr.Employee.RepositoryInterfaces;
using ONEVO.Application.Features.CoreHr.EmployeeAuthority.ServiceInterfaces;
using ONEVO.Application.Features.OrgStructure.RepositoryInterfaces;
using ONEVO.Application.Features.TimeAttendance.DTOs.Responses;
using ONEVO.Application.Features.TimeAttendance.RepositoryInterfaces;
using ONEVO.Application.Features.TimeAttendance.Services;
using ONEVO.Application.Features.TimeAttendance.Team.DTOs;
using ONEVO.Application.Features.TimeAttendance.Team.Services;
using ONEVO.Application.Features.WorkManagement.Forecast.DTOs;
using ONEVO.Application.Features.WorkManagement.Forecast.Queries.GetModuleForecast;
using ONEVO.Application.Features.WorkManagement.Monitoring.Services;

namespace ONEVO.Application.Features.TimeAttendance.Team.Queries;

/// <summary>
/// Employee overtime forecast (spec 2026-11-08 §4b). Population is Team Status's and Work vs task hours' (CoveredTeam:
/// attendance:read TimeTrackingRead coverage, the actor stripped). Per employee, the open estimated tasks due today or
/// later (projects of the viewer's legal entity) are run through the monitor's EDF windows at the employee's 4-week task rate
/// (<see cref="OvertimeForecast.EmployeeWindows"/>, shares from <see cref="ProjectMonitorRules.RemainingShare"/>), on the
/// legal entity's working calendar. With no such task the employee is on_track with zeros.
/// </summary>
public sealed class GetTeamEmployeeForecastQueryHandler(
    ICurrentUser currentUser,
    IEmployeeRepository employees,
    ILegalEntityRepository legalEntities,
    IEmployeeAuthorityResolver authority,
    IAttendanceReadRepository attendance,
    IConversionRateReader rates,
    IForecastTaskReader tasks,
    IDateTimeProvider clock)
    : IRequestHandler<GetTeamEmployeeForecastQuery, Result<TeamEmployeeForecastResponse>>
{
    public static readonly IReadOnlyList<string> Sorts = ["overtime", "requiredRate", "rate", "name"];
    private const string NotFoundMessage = "Current employee record was not found.";

    private sealed record Row(TeamEmployeeForecastItem Item, ForecastFigures Figures);

    public async Task<Result<TeamEmployeeForecastResponse>> Handle(GetTeamEmployeeForecastQuery query, CancellationToken ct)
    {
        if (!currentUser.IsAuthenticated || !currentUser.HasPermission(CoveredTeam.AttendanceReadPermission))
            return Result<TeamEmployeeForecastResponse>.Forbidden();
        if (query.Status is { } s && !ForecastStatuses.All.Contains(s))
            return Result<TeamEmployeeForecastResponse>.UnprocessableEntity("status must be on_track, raise_rate, overtime or no_history.");
        var sort = string.IsNullOrWhiteSpace(query.Sort) ? "overtime" : query.Sort;
        if (!Sorts.Contains(sort))
            return Result<TeamEmployeeForecastResponse>.UnprocessableEntity("sort must be overtime, requiredRate, rate or name.");
        var (page, pageSize) = ForecastQueryOptions.Clamp(query.Page, query.PageSize);

        var tenantId = currentUser.TenantId;
        var actor = await employees.GetDefaultForUserAsync(tenantId, currentUser.UserId, ct);
        if (actor?.LegalEntityId is not Guid legalEntityId)
            return Result<TeamEmployeeForecastResponse>.NotFound(NotFoundMessage);
        var legalEntity = await legalEntities.GetByIdForTenantAsync(tenantId, legalEntityId, ct);
        if (legalEntity is null)
            return Result<TeamEmployeeForecastResponse>.NotFound(NotFoundMessage);

        var today = AttendanceScheduleResolver.Resolve(legalEntity, clock.UtcNow).WorkDate;
        var calendar = WorkCapacityCalculator.FromLegalEntity(legalEntity);
        var covered = await CoveredTeam.CoveredEmployeeIdsAsync(authority, currentUser.UserId, legalEntityId, actor.Id, ct);

        IReadOnlyDictionary<Guid, AttendanceHistoryEmployee> identities = new Dictionary<Guid, AttendanceHistoryEmployee>();
        IReadOnlyList<ForecastOpenTask> openTasks = [];
        IReadOnlyList<ForecastMemberModule> memberModules = [];
        if (covered.Count > 0)
        {
            identities = await attendance.ListEmployeeIdentitiesAsync(tenantId, legalEntityId, covered, ct);
            openTasks = await tasks.ListOpenAssignedAsync(tenantId, legalEntityId, covered, ct);
            memberModules = await tasks.ListMemberModulesAsync(tenantId, legalEntityId, covered, ct);
        }
        var rateSet = await rates.ReadAsync(tenantId, legalEntity, covered, ct);
        var tasksByEmployee = openTasks.ToLookup(t => t.EmployeeId);
        var membershipsByEmployee = memberModules.ToLookup(m => m.EmployeeId);

        var rows = covered.Select(id =>
        {
            identities.TryGetValue(id, out var identity);
            var rate = rateSet.ByEmployee.GetValueOrDefault(id);
            var mine = tasksByEmployee[id].ToList();
            var window = OvertimeForecast.EmployeeWindows(
                mine.Where(t => t.DueDate is not null && t.EstimatedHours is > 0)
                    .Select(t => (t.DueDate!.Value, Share(t))),
                calendar, today, rate?.Rate);
            var figures = window?.Figures
                ?? new ForecastFigures(0m, 0m, rate?.Rate, 0m, 0m, 0m, ForecastStatuses.OnTrack);
            var status = window?.Status ?? ForecastStatuses.OnTrack;
            var requiredRate = window is null ? 0m : window.RequiredRate;

            var modules = mine.GroupBy(t => t.ModuleId)
                .Select(g => new TeamEmployeeForecastModule(g.Key, g.First().ProjectId, g.First().ProjectName, g.First().ModuleTitle,
                    g.First().ModuleEndDate, ForecastRounding.Hours(g.Where(t => t.EstimatedHours is > 0).Sum(Share)), g.Count()))
                .Concat(membershipsByEmployee[id]
                    .Where(m => mine.All(t => t.ModuleId != m.ModuleId))
                    .Select(m => new TeamEmployeeForecastModule(m.ModuleId, m.ProjectId, m.ProjectName, m.ModuleTitle, m.ModuleEndDate, 0m, 0)))
                .OrderByDescending(m => m.RemainingHours).ThenBy(m => m.EndDate).ThenBy(m => m.Title, StringComparer.OrdinalIgnoreCase)
                .ToList();

            var item = new TeamEmployeeForecastItem(
                id, identity?.DisplayName ?? "Unknown", identity?.AvatarFileId, identity?.Department,
                rate?.WorkedMinutes ?? 0, rate?.TaskMinutes ?? 0, ForecastRounding.Rate(rate?.Rate),
                window?.Deadline, ForecastRounding.Hours(figures.Remaining), ForecastRounding.Hours(figures.Capacity),
                ForecastRounding.Hours(figures.Predicted), ForecastRounding.Rate(requiredRate), ForecastRounding.Hours(figures.OvertimeHours),
                status, modules);
            return new Row(item, figures);
        }).ToList();

        var totalRemaining = rows.Sum(r => r.Figures.Remaining);
        var totalCapacity = rows.Sum(r => r.Figures.Capacity);
        var totals = new TeamEmployeeForecastTotals(
            rows.Count,
            rows.Count(r => r.Item.Status == ForecastStatuses.OnTrack),
            rows.Count(r => r.Item.Status == ForecastStatuses.RaiseRate),
            rows.Count(r => r.Item.Status == ForecastStatuses.Overtime),
            rows.Count(r => r.Item.Status == ForecastStatuses.NoHistory),
            ForecastRounding.Hours(totalRemaining), ForecastRounding.Hours(totalCapacity),
            ForecastRounding.Hours(rows.Sum(r => r.Figures.Predicted)), ForecastRounding.Hours(rows.Sum(r => r.Figures.OvertimeHours)),
            ForecastRounding.Rate(rateSet.Pool(covered).Rate),
            ForecastRounding.Rate(totalCapacity > 0 ? totalRemaining / totalCapacity : null));

        IEnumerable<TeamEmployeeForecastItem> selected = rows.Select(r => r.Item);
        if (query.Status is { } status)
            selected = selected.Where(i => i.Status == status);
        if (!string.IsNullOrWhiteSpace(query.Search))
        {
            var term = query.Search.Trim();
            selected = selected.Where(i => i.Name.Contains(term, StringComparison.OrdinalIgnoreCase));
        }
        var filtered = Sort(selected, sort).ToList();

        return Result<TeamEmployeeForecastResponse>.Success(new TeamEmployeeForecastResponse(
            rateSet.Today, rateSet.WindowStart, rateSet.WindowEnd, totals, rateSet.Trend(covered),
            filtered.Skip((page - 1) * pageSize).Take(pageSize).ToList(), filtered.Count, page, pageSize));
    }

    private static decimal Share(ForecastOpenTask t)
        => ProjectMonitorRules.RemainingShare(t.EstimatedHours ?? 0m, t.CompletedHours, t.AssigneeCount);

    private static IEnumerable<TeamEmployeeForecastItem> Sort(IEnumerable<TeamEmployeeForecastItem> items, string sort) => sort switch
    {
        "requiredRate" => items.OrderByDescending(i => i.RequiredRate ?? decimal.MaxValue).ThenBy(i => i.Name, StringComparer.OrdinalIgnoreCase),
        "rate" => items.OrderBy(i => i.ConversionRate is null).ThenBy(i => i.ConversionRate).ThenBy(i => i.Name, StringComparer.OrdinalIgnoreCase),
        "name" => items.OrderBy(i => i.Name, StringComparer.OrdinalIgnoreCase),
        _ => items.OrderByDescending(i => i.OvertimeHours)
            .ThenByDescending(i => ForecastStatuses.Severity(i.Status))
            .ThenByDescending(i => i.RequiredRate ?? decimal.MaxValue)
            .ThenBy(i => i.Name, StringComparer.OrdinalIgnoreCase),
    };
}
