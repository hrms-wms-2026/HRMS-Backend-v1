using MediatR;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.WorkManagement.Common.Services;
using ONEVO.Application.Features.WorkManagement.Planner.Contracts;
using ONEVO.Application.Features.WorkManagement.Planner.Services;

namespace ONEVO.Application.Features.WorkManagement.Planner.Commands;

/// <summary>GET /planner/me?date=: the caller's day (today when no date).</summary>
public sealed record GetMyPlannerDayQuery(DateOnly? Date) : IRequest<Result<PlannerDayResponse>>;

/// <summary>GET /planner/employees/{employeeId}?date=: an employee's day, for the employee or a reporting-line manager.</summary>
public sealed record GetEmployeePlannerDayQuery(Guid EmployeeId, DateOnly? Date) : IRequest<Result<PlannerDayResponse>>;

public sealed record CreatePlannedBlockCommand(CreatePlannedBlockRequest Request) : IRequest<Result<PlannerDayResponse>>;

public sealed record MovePlannedBlockCommand(Guid BlockId, MovePlannedBlockRequest Request) : IRequest<Result<PlannerDayResponse>>;

public sealed record DeletePlannedBlockCommand(Guid BlockId) : IRequest<Result<PlannerDayResponse>>;

/// <summary>Resolves the caller's employee and hands each request to <see cref="PlannerService"/>.</summary>
public sealed class PlannerRequestHandler :
    IRequestHandler<GetMyPlannerDayQuery, Result<PlannerDayResponse>>,
    IRequestHandler<GetEmployeePlannerDayQuery, Result<PlannerDayResponse>>,
    IRequestHandler<CreatePlannedBlockCommand, Result<PlannerDayResponse>>,
    IRequestHandler<MovePlannedBlockCommand, Result<PlannerDayResponse>>,
    IRequestHandler<DeletePlannedBlockCommand, Result<PlannerDayResponse>>
{
    private readonly ICurrentUser _currentUser;
    private readonly ICallerIdentityResolver _identity;
    private readonly PlannerService _planner;

    public PlannerRequestHandler(ICurrentUser currentUser, ICallerIdentityResolver identity, PlannerService planner)
    {
        _currentUser = currentUser;
        _identity = identity;
        _planner = planner;
    }

    public Task<Result<PlannerDayResponse>> Handle(GetMyPlannerDayQuery query, CancellationToken ct)
        => AsCaller((tenant, me) => _planner.GetDayAsync(tenant, me, me, query.Date, ct), ct);

    public Task<Result<PlannerDayResponse>> Handle(GetEmployeePlannerDayQuery query, CancellationToken ct)
        => AsCaller((tenant, me) => _planner.GetDayAsync(tenant, me, query.EmployeeId, query.Date, ct), ct);

    public Task<Result<PlannerDayResponse>> Handle(CreatePlannedBlockCommand command, CancellationToken ct)
        => AsCaller((tenant, me) => _planner.CreateBlockAsync(tenant, me, command.Request, ct), ct);

    public Task<Result<PlannerDayResponse>> Handle(MovePlannedBlockCommand command, CancellationToken ct)
        => AsCaller((tenant, me) => _planner.MoveBlockAsync(tenant, me, command.BlockId, command.Request, ct), ct);

    public Task<Result<PlannerDayResponse>> Handle(DeletePlannedBlockCommand command, CancellationToken ct)
        => AsCaller((tenant, me) => _planner.DeleteBlockAsync(tenant, me, command.BlockId, ct), ct);

    private async Task<Result<PlannerDayResponse>> AsCaller(Func<Guid, Guid, Task<Result<PlannerDayResponse>>> run, CancellationToken ct)
    {
        var caller = _currentUser.IsAuthenticated
            ? await _identity.ResolveCallerEmployeeIdAsync(_currentUser.TenantId, _currentUser.UserId, ct)
            : null;
        return caller is Guid employeeId
            ? await run(_currentUser.TenantId, employeeId)
            : Result<PlannerDayResponse>.Failure("Planner day not found.", 404, PlannerErrorCodes.NotFound);
    }
}
