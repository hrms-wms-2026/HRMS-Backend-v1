using MediatR;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Common.RepositoryInterfaces;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.WorkManagement.CalendarEvents.DTOs.Responses;
using ONEVO.Application.Features.WorkManagement.CalendarEvents.Commands.CreateCalendarEvent;
using ONEVO.Application.Features.WorkManagement.Common.Services;
using ONEVO.Application.Features.WorkManagement.CalendarEvents.RepositoryInterfaces;
using ONEVO.Application.Features.WorkManagement.Objectives.RepositoryInterfaces;
using ONEVO.Domain.Features.WorkManagement.CalendarEvents.Entities;

namespace ONEVO.Application.Features.WorkManagement.CalendarEvents.Commands.UpdateCalendarEvent;

public sealed class UpdateCalendarEventCommandHandler : IRequestHandler<UpdateCalendarEventCommand, Result<CalendarEventResponse>>
{
    private readonly ICurrentUser _currentUser;
    private readonly ICallerIdentityResolver _identity;
    private readonly IObjectiveRepository _objectives;
    private readonly ICalendarEventRepository _calendarEvents;
    private readonly IUnitOfWork _unitOfWork;

    public UpdateCalendarEventCommandHandler(
        ICurrentUser currentUser,
        ICallerIdentityResolver identity,
        IObjectiveRepository objectives,
        ICalendarEventRepository calendarEvents,
        IUnitOfWork unitOfWork)
    {
        _currentUser = currentUser;
        _identity = identity;
        _objectives = objectives;
        _calendarEvents = calendarEvents;
        _unitOfWork = unitOfWork;
    }

    public async Task<Result<CalendarEventResponse>> Handle(UpdateCalendarEventCommand request, CancellationToken ct)
    {
        var actorResult = await ResolveActorAsync(ct);
        if (!actorResult.IsSuccess)
            return Result<CalendarEventResponse>.Failure(actorResult.Error!, actorResult.StatusCode!.Value);

        var tenantId = _currentUser.TenantId;
        var calendarEvent = await _calendarEvents.GetByIdForTenantAsync(tenantId, request.Id, ct);
        if (calendarEvent is null)
            return Result<CalendarEventResponse>.NotFound("Calendar event not found.");
        if (calendarEvent.Status != CalendarEventStatuses.Active)
            return Result<CalendarEventResponse>.Failure("Archived calendar events cannot be edited.");

        var currentMemberships = await _calendarEvents.ListMembershipsForEventAsync(calendarEvent.Id, ct);
        var objectiveIds = request.ObjectiveIds is null
            ? currentMemberships.Select(m => m.ObjectiveId).Distinct().ToList()
            : request.ObjectiveIds.Distinct().ToList();

        var objectives = await _objectives.GetAllByProjectIdAsync(tenantId, calendarEvent.ProjectId, ct);
        var objectiveIdSet = objectives.Select(o => o.Id).ToHashSet();
        var invalidObjectiveIds = objectiveIds.Where(id => !objectiveIdSet.Contains(id)).ToList();
        if (invalidObjectiveIds.Count > 0)
            return Result<CalendarEventResponse>.NotFound($"Objective(s) not found in project: {string.Join(", ", invalidObjectiveIds)}.");

        if (request.ObjectiveIds is not null)
        {
            var activeMemberships = await _calendarEvents.ListActiveMembershipsForObjectivesAsync(tenantId, objectiveIds, ct);
            var conflicts = activeMemberships
                .Where(m => m.CalendarEventId != calendarEvent.Id)
                .GroupBy(m => m.ObjectiveId)
                .Select(g => g.Key)
                .Distinct()
                .ToList();
            if (conflicts.Count > 0)
                return Result<CalendarEventResponse>.Conflict($"Objective(s) already belong to another active calendar event: {string.Join(", ", conflicts)}.");
        }

        var desiredObjectiveIds = objectiveIds.ToHashSet();
        var membershipsToRemove = currentMemberships.Where(m => !desiredObjectiveIds.Contains(m.ObjectiveId)).ToList();
        var existingObjectiveIds = currentMemberships.Select(m => m.ObjectiveId).ToHashSet();
        var now = DateTimeOffset.UtcNow;
        var membershipsToAdd = objectiveIds
            .Where(id => !existingObjectiveIds.Contains(id))
            .Select(id => new CalendarEventObjective
            {
                Id = Guid.NewGuid(),
                CalendarEventId = calendarEvent.Id,
                ObjectiveId = id,
                AddedAt = now
            })
            .ToList();

        calendarEvent.Name = request.Name is null ? calendarEvent.Name : request.Name.Trim();
        calendarEvent.Color = request.Color is null ? calendarEvent.Color : request.Color.Trim();

        await _unitOfWork.ExecuteInTransactionAsync(async innerCt =>
        {
            _calendarEvents.Update(calendarEvent);
            _calendarEvents.RemoveMemberships(membershipsToRemove);
            await _calendarEvents.AddMembershipsAsync(membershipsToAdd, innerCt);
            await _unitOfWork.SaveChangesAsync(innerCt);
            return true;
        }, ct);

        return Result<CalendarEventResponse>.Success(
            CreateCalendarEventCommandHandler.ToResponse(calendarEvent, objectiveIds));
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

    private sealed record ActorResult(bool IsSuccess, Guid EmployeeId, string? Error, int? StatusCode)
    {
        public static ActorResult Success(Guid employeeId) => new(true, employeeId, null, null);
        public static ActorResult Failure(string error, int statusCode) => new(false, Guid.Empty, error, statusCode);
    }
}
