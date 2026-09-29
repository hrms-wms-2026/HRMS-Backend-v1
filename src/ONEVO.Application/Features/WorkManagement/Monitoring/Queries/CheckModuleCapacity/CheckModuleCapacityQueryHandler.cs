using MediatR;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.WorkManagement.Monitoring.DTOs;
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

    public CheckModuleCapacityQueryHandler(
        IProjectMonitorCallerResolver callers, IWorkCalendarResolver calendars, IObjectiveRepository objectives,
        IProjectMemberRepository members, IDateTimeProvider clock)
    {
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

        var people = new HashSet<Guid>(query.MemberEmployeeIds ?? []);
        var completedHours = 0m;
        var title = "This module";
        var existing = query.ModuleId is { } moduleId
            ? await _objectives.GetByIdForTenantAsync(tenantId, moduleId, ct)
            : null;
        if (existing is not null && existing.ProjectId == query.ProjectId)
        {
            foreach (var member in await _members.ListActiveForObjectiveAsync(tenantId, existing.Id, ct))
                people.Add(member.EmployeeId);
            people.Add(existing.OwnerId);
            completedHours = existing.CompletedHours;
            title = existing.Title;
        }
        else
        {
            people.Add(caller.Value.EmployeeId);
        }

        var calendar = await _calendars.ForProjectAsync(tenantId, query.ProjectId, ct);
        var module = new MonitorModule(query.ModuleId ?? Guid.Empty, null, null, title, query.StartDate, query.EndDate,
            query.AllocatedHours, completedHours, IsAchieved: false, people.Count);

        var warnings = new[]
            {
                ProjectMonitorRules.ModuleOverCapacity(module, calendar),
                ProjectMonitorRules.ModuleCapacityShortfall(module, calendar, _clock.Today)
            }
            .OfType<MonitorFinding>()
            .Take(1)   // over capacity already implies the shortfall; show the planning problem only
            .Select(f => new MonitorWarning(f.RuleCode, f.Message))
            .ToList();

        return Result<ModuleCapacityCheckResponse>.Success(new ModuleCapacityCheckResponse(
            calendar.DailyHours, calendar.WorkingDaysBetween(query.StartDate, query.EndDate), people.Count,
            calendar.Capacity(people.Count, query.StartDate, query.EndDate), query.AllocatedHours, warnings));
    }
}
