using MediatR;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.WorkManagement.Hierarchy;
using ONEVO.Application.Features.WorkManagement.Monitoring.DTOs;
using ONEVO.Application.Features.WorkManagement.Monitoring.RepositoryInterfaces;
using ONEVO.Application.Features.WorkManagement.Monitoring.Services;
using ONEVO.Application.Features.WorkManagement.Objectives.RepositoryInterfaces;
using ONEVO.Application.Features.WorkManagement.ProjectMembers.RepositoryInterfaces;

namespace ONEVO.Application.Features.WorkManagement.Monitoring.Queries.CheckModuleCapacity;

public sealed class CheckModuleCapacityQueryHandler : IRequestHandler<CheckModuleCapacityQuery, Result<ModuleCapacityCheckResponse>>
{
    private readonly IProjectMonitorCallerResolver _callers;
    private readonly IWorkCalendarResolver _calendars;
    private readonly IObjectiveRepository _objectives;
    private readonly IProjectMemberRepository _members;
    private readonly IDateTimeProvider _clock;
    private readonly IConversionRateReader _rates;
    private readonly ITaskRateSettingRepository _rateSettings;

    public CheckModuleCapacityQueryHandler(
        IProjectMonitorCallerResolver callers, IWorkCalendarResolver calendars, IObjectiveRepository objectives,
        IProjectMemberRepository members, IDateTimeProvider clock, IConversionRateReader rates,
        ITaskRateSettingRepository rateSettings)
    {
        _rateSettings = rateSettings;
        _rates = rates;
        _callers = callers;
        _calendars = calendars;
        _objectives = objectives;
        _members = members;
        _clock = clock;
    }

    public async Task<Result<ModuleCapacityCheckResponse>> Handle(CheckModuleCapacityQuery query, CancellationToken ct)
    {
        var caller = await _callers.ResolveAsync(query.ProjectId, ct);
        if (!caller.IsSuccess)
            return Result<ModuleCapacityCheckResponse>.Failure(caller.Error!, caller.StatusCode ?? 403);
        var tenantId = caller.Value!.TenantId;

        IReadOnlySet<Guid> people;
        // Only the project's active members (and the caller) are pooled into the rate: an arbitrary employee id typed
        // into the form must not reveal anyone's task rate. Others still count as capacity.
        var projectMembers = (await _members.ListActiveForProjectAsync(tenantId, query.ProjectId, ct)).ToList();
        var pooledAllowed = projectMembers.Select(m => m.EmployeeId).Append(caller.Value.EmployeeId).ToHashSet();
        var completedHours = 0m;
        var title = "This module";
        var existing = query.ModuleId is { } moduleId
            ? await _objectives.GetByIdForTenantAsync(tenantId, moduleId, ct)
            : null;
        if (existing is not null && existing.ProjectId == query.ProjectId)
        {
            // Editing: the allocation is split across the sub-Modules, so everyone at or below counts.
            var tree = new ProjectModuleTree(await _objectives.GetAllByProjectIdAsync(tenantId, query.ProjectId, ct));
            var membersByModule = projectMembers
                .GroupBy(m => m.ObjectiveId)
                .ToDictionary(g => g.Key, g => (IReadOnlySet<Guid>)g.Select(m => m.EmployeeId).ToHashSet());
            people = ModuleManpower.People(tree, existing.Id, membersByModule, query.MemberEmployeeIds);
            completedHours = existing.CompletedHours;
            title = existing.Title;
        }
        else
        {
            // Creating: a new Module has no sub-Modules yet - the picked members plus the creator.
            people = new HashSet<Guid>(query.MemberEmployeeIds ?? []) { caller.Value.EmployeeId };
        }
        var manpower = people.Count;

        // Same prediction as the hourly monitor: capacity x the team's pooled 4-week task rate.
        var legalEntity = await _calendars.LegalEntityForProjectAsync(tenantId, query.ProjectId, ct);
        var calendar = WorkCapacityCalculator.FromLegalEntity(legalEntity);
        var maxTaskRate = await _rateSettings.MaxTaskRateAsync(tenantId, legalEntity?.Id, ct);
        var pooled = people.Where(pooledAllowed.Contains).ToList();
        var pool = (await _rates.ReadAsync(tenantId, legalEntity, pooled, ct)).Pool(pooled);
        // The caller controls the form's numbers, so warnings on a small pool would be an oracle for its rate: below
        // MinRatePool contributors the check assumes 100%, exactly as without history.
        var rate = pool.Contributors >= ProjectMonitorRules.MinRatePool ? pool.Rate : null;
        var module = new MonitorModule(query.ModuleId ?? Guid.Empty, null, null, title, query.StartDate, query.EndDate,
            query.AllocatedHours, completedHours, IsAchieved: false, manpower, rate, pool.Contributors);
        var disclose = rate is not null;

        var warnings = new[]
            {
                ProjectMonitorRules.ModuleOverCapacity(module, calendar, maxTaskRate),
                ProjectMonitorRules.ModuleCapacityShortfall(module, calendar, _clock.Today, maxTaskRate)
            }
            .OfType<MonitorFinding>()
            .Take(1)   // over capacity already implies the shortfall; show the planning problem only
            .Select(f => new MonitorWarning(f.RuleCode, f.Message))
            .ToList();

        var capacity = calendar.Capacity(manpower, query.StartDate, query.EndDate);
        return Result<ModuleCapacityCheckResponse>.Success(new ModuleCapacityCheckResponse(
            calendar.DailyHours, calendar.WorkingDaysBetween(query.StartDate, query.EndDate), manpower,
            capacity, query.AllocatedHours, warnings,
            disclose ? Math.Round(rate!.Value, 4) : null, disclose ? Math.Round(capacity * rate!.Value, 2) : null));
    }
}
