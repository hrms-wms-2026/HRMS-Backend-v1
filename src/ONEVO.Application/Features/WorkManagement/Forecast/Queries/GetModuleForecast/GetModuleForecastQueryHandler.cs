using MediatR;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.CoreHr.Employee.RepositoryInterfaces;
using ONEVO.Application.Features.OrgStructure.RepositoryInterfaces;
using ONEVO.Application.Features.TimeAttendance.DTOs.Responses;
using ONEVO.Application.Features.TimeAttendance.RepositoryInterfaces;
using ONEVO.Application.Features.TimeAttendance.Services;
using ONEVO.Application.Features.WorkManagement.Forecast.DTOs;
using ONEVO.Application.Features.WorkManagement.Hierarchy;
using ONEVO.Application.Features.WorkManagement.Monitoring.RepositoryInterfaces;
using ONEVO.Application.Features.WorkManagement.Monitoring.Services;
using ONEVO.Application.Features.WorkManagement.Objectives.RepositoryInterfaces;
using ONEVO.Application.Features.WorkManagement.ProjectMembers.RepositoryInterfaces;
using ONEVO.Application.Features.WorkManagement.Projects.RepositoryInterfaces;
using ONEVO.Domain.Features.WorkManagement.Objectives.Entities;
using ONEVO.Domain.Features.WorkManagement.Projects.Entities;

namespace ONEVO.Application.Features.WorkManagement.Forecast.Queries.GetModuleForecast;

/// <summary>
/// Module overtime forecast (spec 2026-11-08 §4a). Population: the Modules the viewer leads under Work Management's
/// relationship rules - every Module of a project they lead, otherwise the Modules at or below one they own (effective
/// owner, the "Work I Lead" relationship) - in active, not-achieved, non-system projects of the viewer's legal entity;
/// only Modules that are active, not achieved and end today or later. Plain membership does not qualify: the widget
/// shows every member's individual task rate, which is a lead's view, not a peer's. Each Module's figures come from
/// <see cref="OvertimeForecast"/> with the pooled 4-week rate of its manpower (ModuleManpower), exactly like the monitor's
/// ModuleCapacityShortfall rule, so the widget and the Tree dots agree.
/// </summary>
public sealed class GetModuleForecastQueryHandler(
    ICurrentUser currentUser,
    IEmployeeRepository employees,
    ILegalEntityRepository legalEntities,
    IProjectRepository projects,
    IObjectiveRepository objectives,
    IProjectMemberRepository members,
    IConversionRateReader rates,
    IAttendanceReadRepository attendance,
    IDateTimeProvider clock,
    ITaskRateSettingRepository rateSettings)
    : IRequestHandler<GetModuleForecastQuery, Result<ModuleForecastResponse>>
{
    public static readonly IReadOnlyList<string> Sorts = ["overtime", "requiredRate", "endDate", "name"];
    private const string NotFoundMessage = "Current employee record was not found.";
    private const string PathSeparator = " › ";

    private sealed record Row(Objective Module, Project Project, string Path, IReadOnlySet<Guid> People, PooledRate Rate,
        ForecastFigures Figures, int WorkingDaysLeft, bool IsTopMost);

    public async Task<Result<ModuleForecastResponse>> Handle(GetModuleForecastQuery query, CancellationToken ct)
    {
        if (!currentUser.IsAuthenticated || currentUser.TenantId == Guid.Empty)
            return Result<ModuleForecastResponse>.Forbidden("Authentication required.");
        if (query.Status is { } s && !ForecastStatuses.All.Contains(s))
            return Result<ModuleForecastResponse>.UnprocessableEntity("status must be on_track, raise_rate, overtime or no_history.");
        var sort = string.IsNullOrWhiteSpace(query.Sort) ? "overtime" : query.Sort;
        if (!Sorts.Contains(sort))
            return Result<ModuleForecastResponse>.UnprocessableEntity("sort must be overtime, requiredRate, endDate or name.");
        var (page, pageSize) = ForecastQueryOptions.Clamp(query.Page, query.PageSize);

        var tenantId = currentUser.TenantId;
        var actor = await employees.GetDefaultForUserAsync(tenantId, currentUser.UserId, ct);
        if (actor?.LegalEntityId is not Guid legalEntityId)
            return Result<ModuleForecastResponse>.NotFound(NotFoundMessage);
        var legalEntity = await legalEntities.GetByIdForTenantAsync(tenantId, legalEntityId, ct);
        if (legalEntity is null)
            return Result<ModuleForecastResponse>.NotFound(NotFoundMessage);

        var today = AttendanceScheduleResolver.Resolve(legalEntity, clock.UtcNow).WorkDate;
        var calendar = WorkCapacityCalculator.FromLegalEntity(legalEntity);
        // totals.maxTaskRate = the viewer's Legal Entity limit (the chart line); each module uses its project's own.
        var maxTaskRate = await rateSettings.MaxTaskRateAsync(tenantId, legalEntityId, ct);
        var limitByLegalEntity = new Dictionary<Guid, decimal> { [legalEntityId] = maxTaskRate };

        // Relationship-based access, evaluated in bulk: projects the viewer is a member of (a lead holds a membership)
        // and owned Modules give the candidate projects.
        var memberships = (await members.ListActiveForEmployeeAsync(tenantId, actor.Id, ct)).Where(m => m.IsActive).ToList();
        var owned = await objectives.ListActiveOwnedIdsAsync(tenantId, actor.Id, legalEntityId, ct);
        var projectIds = memberships.Select(m => m.ProjectId).Concat(owned.Select(o => o.ProjectId)).Distinct().ToList();
        var visibleProjects = projectIds.Count == 0
            ? []
            : (await projects.GetActiveByIdsForTenantAsync(tenantId, projectIds, ct))
                .Where(p => p.IsActive && !p.IsDeleted && !p.IsAchieved && p.SystemPurpose is null && p.OwningLegalEntityId == legalEntityId)
                .OrderBy(p => p.Name, StringComparer.OrdinalIgnoreCase)
                .ToList();

        var candidates = new List<(Objective Module, Project Project, string Path, IReadOnlySet<Guid> People, ProjectModuleTree Tree)>();
        foreach (var project in visibleProjects)
        {
            var modules = (await objectives.GetAllByProjectIdAsync(tenantId, project.Id, ct)).Where(o => o.IsActive && !o.IsDeleted).ToList();
            var tree = new ProjectModuleTree(modules);
            IReadOnlySet<Guid> readable = project.LeadId == actor.Id
                ? modules.Select(m => m.Id).ToHashSet()
                : tree.AtOrBelow(owned.Where(o => o.ProjectId == project.Id).Select(o => o.ObjectiveId));
            var membersByModule = (await members.ListActiveForProjectAsync(tenantId, project.Id, ct))
                .GroupBy(m => m.ObjectiveId)
                .ToDictionary(g => g.Key, g => (IReadOnlySet<Guid>)g.Select(m => m.EmployeeId).ToHashSet());

            foreach (var module in modules.Where(m => readable.Contains(m.Id) && !m.IsAchieved && m.EndDate >= today))
                candidates.Add((module, project, PathOf(tree, module), ModuleManpower.People(tree, module.Id, membersByModule), tree));
        }

        foreach (var owningLegalEntityId in visibleProjects.Select(p => p.OwningLegalEntityId).Distinct())
            if (!limitByLegalEntity.ContainsKey(owningLegalEntityId))
                limitByLegalEntity[owningLegalEntityId] = await rateSettings.MaxTaskRateAsync(tenantId, owningLegalEntityId, ct);

        var rateSet = await rates.ReadAsync(tenantId, legalEntity, candidates.SelectMany(c => c.People).ToHashSet(), ct);
        var listedIds = candidates.Select(c => c.Module.Id).ToHashSet();
        var rows = candidates.Select(c =>
        {
            var from = today > c.Module.StartDate ? today : c.Module.StartDate;
            var people = Math.Max(1, c.People.Count);
            var pooled = rateSet.Pool(c.People);
            var figures = OvertimeForecast.Compute(
                c.Module.AllocatedHours - c.Module.CompletedHours, calendar.Capacity(people, from, c.Module.EndDate), pooled.Rate,
                limitByLegalEntity[c.Project.OwningLegalEntityId]);
            var topMost = !c.Tree.AncestorChain(c.Module.Id).Skip(1).Any(a => listedIds.Contains(a.Id));
            return new Row(c.Module, c.Project, c.Path, c.People, pooled, figures,
                calendar.WorkingDaysBetween(from, c.Module.EndDate), topMost);
        }).ToList();

        var top = rows.Where(r => r.IsTopMost).ToList();
        var totalRemaining = top.Sum(r => r.Figures.Remaining);
        var totalCapacity = top.Sum(r => r.Figures.Capacity);
        var totals = new ModuleForecastTotals(
            rows.Count,
            rows.Count(r => r.Figures.Status == ForecastStatuses.OnTrack),
            rows.Count(r => r.Figures.Status == ForecastStatuses.RaiseRate),
            rows.Count(r => r.Figures.Status == ForecastStatuses.Overload),
            rows.Count(r => r.Figures.Status == ForecastStatuses.NoHistory),
            ForecastRounding.Hours(top.Sum(r => r.Module.AllocatedHours)),
            ForecastRounding.Hours(top.Sum(r => r.Module.CompletedHours)),
            ForecastRounding.Hours(totalRemaining),
            ForecastRounding.Hours(totalCapacity),
            ForecastRounding.Hours(top.Sum(r => r.Figures.Predicted)),
            ForecastRounding.Hours(top.Sum(r => r.Figures.OvertimeHours)),
            ForecastRounding.Rate(rateSet.Pool(top.SelectMany(r => r.People)).Rate),
            ForecastRounding.Rate(totalCapacity > 0 ? totalRemaining / totalCapacity : null),
            maxTaskRate);

        var projectBars = rows.GroupBy(r => r.Project.Id)
            .Select(g =>
            {
                var heads = g.Where(r => r.IsTopMost).ToList();
                return new ModuleForecastProject(g.Key, g.First().Project.Name,
                    ForecastRounding.Hours(heads.Sum(r => r.Module.AllocatedHours)),
                    ForecastRounding.Hours(heads.Sum(r => r.Figures.Remaining)),
                    ForecastRounding.Hours(heads.Sum(r => r.Figures.Predicted)),
                    ForecastRounding.Hours(heads.Sum(r => r.Figures.Capacity)),
                    ForecastRounding.Hours(heads.Sum(r => r.Figures.OvertimeHours)),
                    g.Count(r => r.Figures.Status is ForecastStatuses.RaiseRate or ForecastStatuses.Overload));
            })
            .OrderByDescending(p => p.OvertimeHours).ThenBy(p => p.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();

        // Totals, project bars and the trend cover the whole population; projectId, status and search narrow the rows only.
        IEnumerable<Row> selected = rows;
        if (query.ProjectId is Guid only)
            selected = selected.Where(r => r.Project.Id == only);
        if (query.Status is { } status)
            selected = selected.Where(r => r.Figures.Status == status);
        if (!string.IsNullOrWhiteSpace(query.Search))
        {
            var term = query.Search.Trim();
            selected = selected.Where(r => r.Module.Title.Contains(term, StringComparison.OrdinalIgnoreCase)
                || r.Path.Contains(term, StringComparison.OrdinalIgnoreCase)
                || r.Project.Name.Contains(term, StringComparison.OrdinalIgnoreCase));
        }
        var filtered = Sort(selected, sort).ToList();
        var pageRows = filtered.Skip((page - 1) * pageSize).Take(pageSize).ToList();

        var pagePeople = pageRows.SelectMany(r => r.People).ToHashSet();
        IReadOnlyDictionary<Guid, AttendanceHistoryEmployee> identities = pagePeople.Count == 0
            ? new Dictionary<Guid, AttendanceHistoryEmployee>()
            : await attendance.ListEmployeeIdentitiesAsync(tenantId, legalEntityId, pagePeople, ct);

        var items = pageRows.Select(r => new ModuleForecastItem(
            r.Module.Id, r.Project.Id, r.Project.Name, r.Module.Title, r.Path, r.Module.StartDate, r.Module.EndDate, r.WorkingDaysLeft,
            ForecastRounding.Hours(r.Module.AllocatedHours), ForecastRounding.Hours(r.Module.CompletedHours),
            ForecastRounding.Hours(r.Figures.Remaining), ForecastRounding.Hours(r.Figures.Capacity),
            ForecastRounding.Rate(r.Figures.Rate), ForecastRounding.Hours(r.Figures.Predicted), ForecastRounding.Rate(r.Figures.RequiredRate),
            ForecastRounding.Hours(r.Figures.OvertimeHours), r.Figures.Status, r.People.Count,
            limitByLegalEntity[r.Project.OwningLegalEntityId],
            r.People.Select(id =>
                {
                    identities.TryGetValue(id, out var identity);
                    var rate = rateSet.ByEmployee.GetValueOrDefault(id);
                    return new ModuleForecastMember(id, identity?.DisplayName ?? "Unknown", identity?.AvatarFileId,
                        ForecastRounding.Rate(rate?.Rate), rate?.WorkedMinutes ?? 0, rate?.TaskMinutes ?? 0);
                })
                .OrderBy(m => m.Name, StringComparer.OrdinalIgnoreCase)
                .ToList()))
            .ToList();

        return Result<ModuleForecastResponse>.Success(new ModuleForecastResponse(
            rateSet.Today, rateSet.WindowStart, rateSet.WindowEnd, totals,
            rateSet.Trend(rows.SelectMany(r => r.People)), projectBars, items, filtered.Count, page, pageSize));
    }

    private static IEnumerable<Row> Sort(IEnumerable<Row> rows, string sort) => sort switch
    {
        "requiredRate" => rows.OrderByDescending(r => r.Figures.RequiredRate ?? decimal.MaxValue)
            .ThenBy(r => r.Module.Title, StringComparer.OrdinalIgnoreCase),
        "endDate" => rows.OrderBy(r => r.Module.EndDate).ThenBy(r => r.Module.Title, StringComparer.OrdinalIgnoreCase),
        "name" => rows.OrderBy(r => r.Module.Title, StringComparer.OrdinalIgnoreCase).ThenBy(r => r.Project.Name, StringComparer.OrdinalIgnoreCase),
        _ => rows.OrderByDescending(r => r.Figures.OvertimeHours)
            .ThenByDescending(r => ForecastStatuses.Severity(r.Figures.Status))
            .ThenByDescending(r => r.Figures.RequiredRate ?? decimal.MaxValue)
            .ThenBy(r => r.Module.Title, StringComparer.OrdinalIgnoreCase),
    };

    /// <summary>Ancestor titles down to the Module, "Parent › Child"; the project's default top Module is left out
    /// unless it is the Module itself.</summary>
    private static string PathOf(ProjectModuleTree tree, Objective module)
        => string.Join(PathSeparator, tree.AncestorChain(module.Id)
            .Where(m => m.Id == module.Id || !m.IsDefault)
            .Reverse()
            .Select(m => m.Title));
}
