using MediatR;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Common.RepositoryInterfaces;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.WorkManagement.CalendarEvents.DTOs.Responses;
using ONEVO.Application.Features.WorkManagement.Common.Services;
using ONEVO.Application.Features.WorkManagement.CalendarEvents.RepositoryInterfaces;
using ONEVO.Application.Features.WorkManagement.Objectives.RepositoryInterfaces;
using ONEVO.Application.Features.WorkManagement.Projects.RepositoryInterfaces;
using ONEVO.Domain.Features.WorkManagement.CalendarEvents.Entities;

namespace ONEVO.Application.Features.WorkManagement.CalendarEvents.Commands.CreateCalendarEvent;

public sealed class CreateCalendarEventCommandHandler : IRequestHandler<CreateCalendarEventCommand, Result<CalendarEventResponse>>
{
    private readonly ICurrentUser _currentUser;
    private readonly ICallerIdentityResolver _identity;
    private readonly IProjectRepository _projects;
    private readonly IObjectiveRepository _objectives;
    private readonly ICalendarEventRepository _calendarEvents;
    private readonly IUnitOfWork _unitOfWork;

    public CreateCalendarEventCommandHandler(
        ICurrentUser currentUser,
        ICallerIdentityResolver identity,
        IProjectRepository projects,
        IObjectiveRepository objectives,
        ICalendarEventRepository calendarEvents,
        IUnitOfWork unitOfWork)
    {
        _currentUser = currentUser;
        _identity = identity;
        _projects = projects;
        _objectives = objectives;
        _calendarEvents = calendarEvents;
        _unitOfWork = unitOfWork;
    }

    public async Task<Result<CalendarEventResponse>> Handle(CreateCalendarEventCommand request, CancellationToken ct)
    {
        var actorResult = await ResolveActorAsync(ct);
        if (!actorResult.IsSuccess)
            return Result<CalendarEventResponse>.Failure(actorResult.Error!, actorResult.StatusCode!.Value);

        var tenantId = _currentUser.TenantId;
        var project = await _projects.GetByIdForTenantAsync(tenantId, request.ProjectId, ct);
        if (project is null)
            return Result<CalendarEventResponse>.NotFound("Project not found.");

        var objectiveIds = request.ObjectiveIds.Distinct().ToList();
        var objectives = await _objectives.GetAllByProjectIdAsync(tenantId, request.ProjectId, ct);
        var objectiveIdSet = objectives.Select(o => o.Id).ToHashSet();
        var invalidObjectiveIds = objectiveIds.Where(id => !objectiveIdSet.Contains(id)).ToList();
        if (invalidObjectiveIds.Count > 0)
            return Result<CalendarEventResponse>.NotFound($"Objective(s) not found in project: {string.Join(", ", invalidObjectiveIds)}.");

        var activeMemberships = await _calendarEvents.ListActiveMembershipsForObjectivesAsync(tenantId, objectiveIds, ct);
        var conflicts = activeMemberships
            .GroupBy(m => m.ObjectiveId)
            .Select(g => g.Key)
            .Distinct()
            .ToList();
        if (conflicts.Count > 0)
            return Result<CalendarEventResponse>.Conflict($"Objective(s) already belong to an active calendar event: {string.Join(", ", conflicts)}.");

        var now = DateTimeOffset.UtcNow;
        var calendarEvent = new CalendarEvent
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            ProjectId = request.ProjectId,
            Name = request.Name.Trim(),
            Color = request.Color.Trim(),
            Status = CalendarEventStatuses.Active,
            CreatedById = actorResult.EmployeeId,
            CreatedAt = now
        };
        var memberships = objectiveIds.Select(objectiveId => new CalendarEventObjective
        {
            Id = Guid.NewGuid(),
            CalendarEventId = calendarEvent.Id,
            ObjectiveId = objectiveId,
            AddedAt = now
        }).ToList();

        await _unitOfWork.ExecuteInTransactionAsync(async innerCt =>
        {
            await _calendarEvents.AddAsync(calendarEvent, innerCt);
            await _calendarEvents.AddMembershipsAsync(memberships, innerCt);
            await _unitOfWork.SaveChangesAsync(innerCt);
            return true;
        }, ct);

        return Result<CalendarEventResponse>.Success(ToResponse(calendarEvent, objectiveIds));
    }

    private async Task<ActorResult> ResolveActorAsync(CancellationToken ct)
    {
        if (!_currentUser.IsAuthenticated)
            return ActorResult.Failure("Authentication required.", 403);

        if (_currentUser.TenantId == Guid.Empty)
            return ActorResult.Failure("Tenant context missing.", 403);

        var employeeId = await _identity.ResolveCallerEmployeeIdAsync(_currentUser.TenantId, _currentUser.UserId, ct);
        return employeeId is null
            ? ActorResult.Failure("No employee record for the current user.", 403)
            : ActorResult.Success(employeeId.Value);
    }

    internal static CalendarEventResponse ToResponse(CalendarEvent calendarEvent, IReadOnlyList<Guid> objectiveIds)
        => new(calendarEvent.Id, calendarEvent.ProjectId, calendarEvent.Name, calendarEvent.Color,
            calendarEvent.Status, objectiveIds, calendarEvent.CreatedAt, calendarEvent.ArchivedById, calendarEvent.ArchivedAt);

    private sealed record ActorResult(bool IsSuccess, Guid EmployeeId, string? Error, int? StatusCode)
    {
        public static ActorResult Success(Guid employeeId) => new(true, employeeId, null, null);
        public static ActorResult Failure(string error, int statusCode) => new(false, Guid.Empty, error, statusCode);
    }
}
