using MediatR;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Common.RepositoryInterfaces;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.Calendar.DTOs.Responses;
using ONEVO.Application.Features.Calendar.RepositoryInterfaces;
using ONEVO.Application.Features.Calendar.Services;

namespace ONEVO.Application.Features.Calendar.Commands.UpdateCalendarEvent;

public sealed class UpdateCalendarEventCommandHandler(
    ICurrentUser currentUser,
    ICalendarEventRepository events,
    ONEVO.Application.Features.CoreHr.Employee.RepositoryInterfaces.IEmployeeRepository employees,
    ICalendarNotificationSender notifications,
    IUnitOfWork unitOfWork)
    : IRequestHandler<UpdateCalendarEventCommand, Result<CalendarEventItem>>
{
    public async Task<Result<CalendarEventItem>> Handle(UpdateCalendarEventCommand request, CancellationToken ct)
    {
        if (!currentUser.IsAuthenticated)
            return Result<CalendarEventItem>.Forbidden();

        if (request.EndDate < request.StartDate)
            return Result<CalendarEventItem>.Failure("End date cannot be before start date.", 400);

        var tenantId = currentUser.TenantId;
        var existing = await events.GetTrackedByIdForTenantAsync(tenantId, request.Id, ct);
        if (existing is null)
            return Result<CalendarEventItem>.NotFound("Calendar event not found.");

        if (existing.CreatedById != currentUser.UserId)
            return Result<CalendarEventItem>.Forbidden("Only the event creator can edit this event.");

        return await unitOfWork.ExecuteInTransactionAsync(async innerCt =>
        {
            existing.Title = request.Title.Trim();
            existing.Description = request.Description;
            existing.StartDate = request.StartDate;
            existing.EndDate = request.EndDate;
            existing.IsAllDay = request.IsAllDay;
            existing.Location = request.Location;
            existing.MeetingLink = request.MeetingLink;
            existing.Color = request.Color;
            existing.Recurrence = request.Recurrence;

            events.Update(existing);
            await unitOfWork.SaveChangesAsync(innerCt);

            var participantsByEvent = await events.GetParticipantsForEventsAsync(tenantId, [existing.Id], innerCt);
            var participantEmployeeIds = participantsByEvent.TryGetValue(existing.Id, out var p) ? p.Select(x => x.EmployeeId).ToList() : [];
            if (participantEmployeeIds.Count > 0)
            {
                var callerEmployee = await employees.GetDefaultForUserAsync(tenantId, currentUser.UserId, innerCt);
                var organizerName = callerEmployee is null ? "Someone" : $"{callerEmployee.FirstName} {callerEmployee.LastName}";
                await notifications.NotifyEventUpdatedAsync(tenantId, existing.Title, participantEmployeeIds, organizerName, innerCt);
            }

            return Result<CalendarEventItem>.Success(new CalendarEventItem(
                existing.Id, existing.Title, existing.Description, existing.StartDate, existing.EndDate,
                existing.SourceType, existing.Color, existing.Recurrence, existing.IsAllDay, existing.Timezone,
                existing.EventStatus, existing.IsPrivate, existing.Location, existing.MeetingLink,
                existing.ExternalSource, existing.CreatedById));
        }, ct);
    }
}
