using MediatR;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Common.RepositoryInterfaces;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.Calendar.DTOs.Responses;
using ONEVO.Application.Features.Calendar.Helpers;
using ONEVO.Application.Features.Calendar.RepositoryInterfaces;
using ONEVO.Application.Features.Calendar.Services;
using ONEVO.Domain.Features.Calendar.Entities;

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

        if (!CalendarEventValidation.IsValidMeetingLink(request.MeetingLink))
            return Result<CalendarEventItem>.Failure("Meeting link must be a valid http(s) URL.", 400);

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

            existing.Revision = await events.IncrementRevisionAsync(tenantId, existing.Id, innerCt);
            events.Update(existing);

            // Spec §3.1: stage every notification BEFORE the single SaveChangesAsync, so the change
            // and its notifications commit together (the sender only stages outbox rows).
            await NotifyUpdatedAsync(tenantId, existing, innerCt);
            await unitOfWork.SaveChangesAsync(innerCt);

            return Result<CalendarEventItem>.Success(new CalendarEventItem(
                existing.Id, existing.Title, existing.Description, existing.StartDate, existing.EndDate,
                existing.SourceType, existing.Color, existing.Recurrence, existing.IsAllDay, existing.Timezone,
                existing.EventStatus, existing.IsPrivate, existing.Location, existing.MeetingLink,
                existing.ExternalSource, existing.CreatedById));
        }, ct);
    }

    private async Task NotifyUpdatedAsync(Guid tenantId, CalendarEvent updated, CancellationToken ct)
    {
        var participants = (await events.GetParticipantsForEventsAsync(tenantId, [updated.Id], ct)).GetValueOrDefault(updated.Id) ?? [];
        var guests = (await events.GetGuestsForEventsAsync(tenantId, [updated.Id], ct)).GetValueOrDefault(updated.Id) ?? [];
        if (participants.Count == 0 && guests.Count == 0)
            return;

        var callerEmployee = await employees.GetDefaultForUserAsync(tenantId, currentUser.UserId, ct);
        var organizerName = callerEmployee is null ? "Someone" : $"{callerEmployee.FirstName} {callerEmployee.LastName}";
        await notifications.NotifyEventUpdatedAsync(
            tenantId, updated.Title, updated.StartDate, updated.Location, updated.MeetingLink,
            participants.Select(p => new CalendarParticipantRecipient(p.EmployeeId, CalendarEmailKeys.Update(updated.Id, updated.Revision, p.Id))).ToList(),
            guests.Select(g => new CalendarGuestRecipient(g.Email, CalendarEmailKeys.Update(updated.Id, updated.Revision, g.Id))).ToList(),
            organizerName, ct);
    }
}
