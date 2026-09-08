using MediatR;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Common.RepositoryInterfaces;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.WorkManagement.CalendarEvents.DTOs.Responses;
using ONEVO.Application.Features.WorkManagement.CalendarEvents.Commands.CreateCalendarEvent;
using ONEVO.Application.Features.WorkManagement.Common.Services;
using ONEVO.Application.Features.WorkManagement.CalendarEvents.RepositoryInterfaces;
using ONEVO.Domain.Features.WorkManagement.CalendarEvents.Entities;

namespace ONEVO.Application.Features.WorkManagement.CalendarEvents.Commands.CloseCalendarEvent;

public sealed class CloseCalendarEventCommandHandler : IRequestHandler<CloseCalendarEventCommand, Result<CalendarEventResponse>>
{
    private readonly ICurrentUser _currentUser;
    private readonly ICallerIdentityResolver _identity;
    private readonly ICalendarEventRepository _calendarEvents;
    private readonly IUnitOfWork _unitOfWork;

    public CloseCalendarEventCommandHandler(
        ICurrentUser currentUser,
        ICallerIdentityResolver identity,
        ICalendarEventRepository calendarEvents,
        IUnitOfWork unitOfWork)
    {
        _currentUser = currentUser;
        _identity = identity;
        _calendarEvents = calendarEvents;
        _unitOfWork = unitOfWork;
    }

    public async Task<Result<CalendarEventResponse>> Handle(CloseCalendarEventCommand request, CancellationToken ct)
    {
        if (!_currentUser.IsAuthenticated)
            return Result<CalendarEventResponse>.Forbidden("Authentication required.");
        if (_currentUser.TenantId == Guid.Empty)
            return Result<CalendarEventResponse>.Forbidden("Tenant context missing.");

        var employeeId = await _identity.ResolveCallerEmployeeIdAsync(_currentUser.TenantId, _currentUser.UserId, ct);
        if (employeeId is null)
            return Result<CalendarEventResponse>.Forbidden("No employee record for the current user.");

        var calendarEvent = await _calendarEvents.GetByIdForTenantAsync(_currentUser.TenantId, request.Id, ct);
        if (calendarEvent is null)
            return Result<CalendarEventResponse>.NotFound("Calendar event not found.");
        if (calendarEvent.Status == CalendarEventStatuses.Archived)
            return Result<CalendarEventResponse>.Success(
                CreateCalendarEventCommandHandler.ToResponse(calendarEvent, Array.Empty<Guid>()));

        var memberships = await _calendarEvents.ListMembershipsForEventAsync(calendarEvent.Id, ct);
        calendarEvent.Status = CalendarEventStatuses.Archived;
        calendarEvent.ArchivedById = employeeId.Value;
        calendarEvent.ArchivedAt = DateTimeOffset.UtcNow;

        await _unitOfWork.ExecuteInTransactionAsync(async innerCt =>
        {
            _calendarEvents.Update(calendarEvent);
            await _unitOfWork.SaveChangesAsync(innerCt);
            return true;
        }, ct);

        return Result<CalendarEventResponse>.Success(
            CreateCalendarEventCommandHandler.ToResponse(calendarEvent, memberships.Select(m => m.ObjectiveId).ToList()));
    }
}
