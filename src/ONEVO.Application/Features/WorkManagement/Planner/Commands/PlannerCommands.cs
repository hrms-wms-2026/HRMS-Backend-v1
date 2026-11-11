using MediatR;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.WorkManagement.Common.Services;
using ONEVO.Application.Features.WorkManagement.Meetings.Contracts;
using ONEVO.Application.Features.WorkManagement.Meetings.RepositoryInterfaces;
using ONEVO.Application.Features.WorkManagement.Meetings.Services;
using ONEVO.Application.Features.WorkManagement.Planner.Contracts;
using ONEVO.Application.Features.WorkManagement.Planner.Services;

namespace ONEVO.Application.Features.WorkManagement.Planner.Commands;

/// <summary>GET /planner/me?date=: the caller's day (today when no date).</summary>
public sealed record GetMyPlannerDayQuery(DateOnly? Date) : IRequest<Result<PlannerDayResponse>>;

/// <summary>GET /planner/employees/{employeeId}?date=: an employee's day, for the employee or a reporting-line manager.</summary>
public sealed record GetEmployeePlannerDayQuery(Guid EmployeeId, DateOnly? Date) : IRequest<Result<PlannerDayResponse>>;

/// <summary>GET /planner/me/range?from=&amp;to=: the caller's days for the calendar grid.</summary>
/// <param name="ForEmployeeId">A principal's days, for their active meeting delegate; null or the caller means the caller's own.</param>
public sealed record GetMyPlannerRangeQuery(DateOnly From, DateOnly To, Guid? ForEmployeeId = null) : IRequest<Result<PlannerRangeResponse>>;

/// <summary>GET /planner/me/focus: what the caller should work on.</summary>
/// <param name="ForEmployeeId">A principal's Focus list, for their active planning delegate; null or the caller means the caller's own.</param>
public sealed record GetMyPlannerFocusQuery(Guid? ForEmployeeId = null) : IRequest<Result<PlannerFocusResponse>>;

/// <param name="ForEmployeeId">The principal, for their active planning delegate; null means the caller's own planner.</param>
public sealed record CreatePlannedBlockCommand(CreatePlannedBlockRequest Request, Guid? ForEmployeeId = null) : IRequest<Result<PlannerDayResponse>>;

public sealed record MovePlannedBlockCommand(Guid BlockId, MovePlannedBlockRequest Request, Guid? ForEmployeeId = null)
    : IRequest<Result<PlannerDayResponse>>;

public sealed record DeletePlannedBlockCommand(Guid BlockId, Guid? ForEmployeeId = null, string? ExpectedVersion = null)
    : IRequest<Result<PlannerDayResponse>>;

/// <summary>GET /planner/employees/{employeeId}/range?from=&amp;to=: one person's days, read-only (team planner, 2026-11-03).</summary>
public sealed record GetEmployeePlannerRangeQuery(Guid EmployeeId, DateOnly From, DateOnly To)
    : IRequest<Result<PlannerPersonRangeResponse>>;

/// <summary>GET /planner/team?date=&amp;managerId=: a manager's direct reports for one day, with detail.</summary>
public sealed record GetTeamPlannerDayQuery(DateOnly? Date, Guid? ManagerEmployeeId) : IRequest<Result<TeamPlannerResponse>>;

/// <summary>GET /planner/team/range?from=&amp;to=&amp;managerId=: the same team over a range, minutes only.</summary>
public sealed record GetTeamPlannerRangeQuery(DateOnly From, DateOnly To, Guid? ManagerEmployeeId) : IRequest<Result<TeamPlannerResponse>>;

/// <summary>Team planner reads (2026-11-03): the caller's employee plus whether they may read others' leave.</summary>
public sealed class TeamPlannerRequestHandler :
    IRequestHandler<GetEmployeePlannerRangeQuery, Result<PlannerPersonRangeResponse>>,
    IRequestHandler<GetTeamPlannerDayQuery, Result<TeamPlannerResponse>>,
    IRequestHandler<GetTeamPlannerRangeQuery, Result<TeamPlannerResponse>>
{
    /// <summary>The permission that shows a team's leave as leave, as on Team Status (My Team spec 8.1.2).</summary>
    public const string LeaveReadPermission = "leave:read";

    private readonly ICurrentUser _currentUser;
    private readonly ICallerIdentityResolver _identity;
    private readonly TeamPlannerService _team;

    public TeamPlannerRequestHandler(ICurrentUser currentUser, ICallerIdentityResolver identity, TeamPlannerService team)
    {
        _currentUser = currentUser;
        _identity = identity;
        _team = team;
    }

    public Task<Result<PlannerPersonRangeResponse>> Handle(GetEmployeePlannerRangeQuery query, CancellationToken ct)
        => AsCaller((tenant, me) => _team.GetPersonRangeAsync(tenant, me, query.EmployeeId, query.From, query.To,
            _currentUser.HasPermission(LeaveReadPermission), ct), ct);

    public Task<Result<TeamPlannerResponse>> Handle(GetTeamPlannerDayQuery query, CancellationToken ct)
        => AsCaller((tenant, me) => _team.GetTeamDayAsync(tenant, me, query.ManagerEmployeeId, query.Date,
            _currentUser.HasPermission(LeaveReadPermission), ct), ct);

    public Task<Result<TeamPlannerResponse>> Handle(GetTeamPlannerRangeQuery query, CancellationToken ct)
        => AsCaller((tenant, me) => _team.GetTeamAsync(tenant, me, query.ManagerEmployeeId, query.From, query.To, detail: false,
            _currentUser.HasPermission(LeaveReadPermission), ct), ct);

    private async Task<Result<T>> AsCaller<T>(Func<Guid, Guid, Task<Result<T>>> run, CancellationToken ct)
    {
        var caller = _currentUser.IsAuthenticated
            ? await _identity.ResolveCallerEmployeeIdAsync(_currentUser.TenantId, _currentUser.UserId, ct)
            : null;
        return caller is Guid employeeId
            ? await run(_currentUser.TenantId, employeeId)
            : Result<T>.Failure("Planner day not found.", 404, PlannerErrorCodes.NotFound);
    }
}

/// <summary>Resolves the caller's employee and hands each request to <see cref="PlannerService"/>.</summary>
public sealed class PlannerRequestHandler :
    IRequestHandler<GetMyPlannerDayQuery, Result<PlannerDayResponse>>,
    IRequestHandler<GetEmployeePlannerDayQuery, Result<PlannerDayResponse>>,
    IRequestHandler<GetMyPlannerRangeQuery, Result<PlannerRangeResponse>>,
    IRequestHandler<GetMyPlannerFocusQuery, Result<PlannerFocusResponse>>,
    IRequestHandler<CreatePlannedBlockCommand, Result<PlannerDayResponse>>,
    IRequestHandler<MovePlannedBlockCommand, Result<PlannerDayResponse>>,
    IRequestHandler<DeletePlannedBlockCommand, Result<PlannerDayResponse>>
{
    private readonly ICurrentUser _currentUser;
    private readonly ICallerIdentityResolver _identity;
    private readonly PlannerService _planner;
    private readonly IMeetingDelegateRepository? _delegates;
    private readonly IDateTimeProvider? _clock;
    private readonly IDelegateAccess? _delegateAccess;

    public PlannerRequestHandler(ICurrentUser currentUser, ICallerIdentityResolver identity, PlannerService planner,
        IMeetingDelegateRepository? delegates = null, IDateTimeProvider? clock = null, IDelegateAccess? delegateAccess = null)
    {
        _delegateAccess = delegateAccess;
        _currentUser = currentUser;
        _identity = identity;
        _planner = planner;
        _delegates = delegates;
        _clock = clock;
    }

    public Task<Result<PlannerDayResponse>> Handle(GetMyPlannerDayQuery query, CancellationToken ct)
        => AsCaller((tenant, me) => _planner.GetDayAsync(tenant, me, me, query.Date, ct), ct);

    public Task<Result<PlannerDayResponse>> Handle(GetEmployeePlannerDayQuery query, CancellationToken ct)
        => AsCaller((tenant, me) => _planner.GetDayAsync(tenant, me, query.EmployeeId, query.Date, ct), ct);

    public Task<Result<PlannerRangeResponse>> Handle(GetMyPlannerRangeQuery query, CancellationToken ct)
        => AsCaller(async (tenant, me) =>
        {
            if (query.ForEmployeeId is not Guid forId || forId == me)
                return await _planner.GetRangeAsync(tenant, me, query.From, query.To, ct);
            // A planning delegate (2026-11-11) gets the principal's editable days; a meeting-only delegate the same days
            // read-only (spec "Seeing the principal's calendar").
            if (_delegateAccess is not null && await _delegateAccess.PlanningSubjectAsync(tenant, me, forId, ct) is not null)
                return await _planner.GetRangeAsync(tenant, forId, query.From, query.To, ct);
            return _delegates is not null && _clock is not null
                && await _delegates.IsActiveDelegateAsync(tenant, forId, me, _clock.UtcNow, ct)
                    ? await _planner.GetRangeAsync(tenant, forId, query.From, query.To, ct, readOnly: true)
                    : Result<PlannerRangeResponse>.Failure("You cannot see this person's planner.", 403, MeetingErrorCodes.DelegateRequired);
        }, ct);

    public Task<Result<PlannerFocusResponse>> Handle(GetMyPlannerFocusQuery query, CancellationToken ct)
        => AsCaller(async (tenant, me) => await SubjectAsync(tenant, me, query.ForEmployeeId, ct) is Guid subject
            ? await _planner.GetFocusAsync(tenant, subject, ct)
            : PlannerService.NotFound<PlannerFocusResponse>(), ct);

    /// <summary>The employee whose planner this request acts on: the caller, or the principal for their active planning
    /// delegate (read from the database every time). Null when <paramref name="forEmployeeId"/> is someone the caller may
    /// not plan for.</summary>
    private async Task<Guid?> SubjectAsync(Guid tenant, Guid me, Guid? forEmployeeId, CancellationToken ct)
    {
        if (forEmployeeId is not Guid forId || forId == me)
            return me;
        return _delegateAccess is not null && await _delegateAccess.PlanningSubjectAsync(tenant, me, forId, ct) is { } subject
            ? subject.EmployeeId
            : null;
    }

    public Task<Result<PlannerDayResponse>> Handle(CreatePlannedBlockCommand command, CancellationToken ct)
        => AsCaller(async (tenant, me) => await SubjectAsync(tenant, me, command.ForEmployeeId, ct) is Guid subject
            ? await _planner.CreateBlockAsync(tenant, subject, command.Request, ct)
            : PlannerService.NotFound<PlannerDayResponse>(), ct);

    public Task<Result<PlannerDayResponse>> Handle(MovePlannedBlockCommand command, CancellationToken ct)
        => AsCaller(async (tenant, me) => await SubjectAsync(tenant, me, command.ForEmployeeId, ct) is Guid subject
            ? await _planner.MoveBlockAsync(tenant, subject, command.BlockId, command.Request, ct)
            : PlannerService.NotFound<PlannerDayResponse>(), ct);

    public Task<Result<PlannerDayResponse>> Handle(DeletePlannedBlockCommand command, CancellationToken ct)
        => AsCaller(async (tenant, me) => await SubjectAsync(tenant, me, command.ForEmployeeId, ct) is Guid subject
            ? await _planner.DeleteBlockAsync(tenant, subject, command.BlockId, ct, command.ExpectedVersion)
            : PlannerService.NotFound<PlannerDayResponse>(), ct);

    private async Task<Result<T>> AsCaller<T>(Func<Guid, Guid, Task<Result<T>>> run, CancellationToken ct)
    {
        var caller = _currentUser.IsAuthenticated
            ? await _identity.ResolveCallerEmployeeIdAsync(_currentUser.TenantId, _currentUser.UserId, ct)
            : null;
        return caller is Guid employeeId
            ? await run(_currentUser.TenantId, employeeId)
            : Result<T>.Failure("Planner day not found.", 404, PlannerErrorCodes.NotFound);
    }
}
