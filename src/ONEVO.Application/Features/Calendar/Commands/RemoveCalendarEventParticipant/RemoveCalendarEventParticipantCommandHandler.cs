using MediatR;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Common.RepositoryInterfaces;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.Calendar.RepositoryInterfaces;
using ONEVO.Application.Features.Calendar.Services;

namespace ONEVO.Application.Features.Calendar.Commands.RemoveCalendarEventParticipant;

public sealed record RemoveCalendarEventParticipantCommand(Guid EventId, Guid EmployeeId) : IRequest<Result>;

/// <summary>Removes one participant from an event that already exists. Organizer-only,
/// matching AddCalendarEventParticipantsCommandHandler. No email is sent for a removal - the
/// removed person still sees the event disappear from their own calendar next load.</summary>
public sealed class RemoveCalendarEventParticipantCommandHandler(
    ICurrentUser currentUser,
    ICalendarEventRepository events,
    IUnitOfWork unitOfWork,
    ICalendarNotificationSender? notifications = null,
    ONEVO.Application.Features.CoreHr.Employee.RepositoryInterfaces.IEmployeeRepository? employees = null)
    : IRequestHandler<RemoveCalendarEventParticipantCommand, Result>
{
    public async Task<Result> Handle(RemoveCalendarEventParticipantCommand request, CancellationToken ct)
    {
        if (!currentUser.IsAuthenticated)
            return Result.Forbidden();

        var tenantId = currentUser.TenantId;
        var existing = await events.GetTrackedByIdForTenantAsync(tenantId, request.EventId, ct);
        if (existing is null)
            return Result.NotFound("Calendar event not found.");

        if (existing.CreatedById != currentUser.UserId)
            return Result.Forbidden("Only the event organizer can remove participants.");

        var participant = await events.GetTrackedParticipantAsync(tenantId, request.EventId, request.EmployeeId, ct);
        if (participant is null)
            return Result.NotFound("This employee is not a participant on this event.");

        events.RemoveParticipant(participant);

        // The removed person hears about it in-app and by email (plan C1 P1-4); staged before the save that writes it.
        if (notifications is not null)
        {
            var organizerName = await CalendarOrganizer.NameAsync(employees, tenantId, currentUser.UserId, ct);
            await notifications.NotifyParticipantRemovedAsync(tenantId, participant.EmployeeId, existing.Title, organizerName, ct);
            await notifications.EmailEventChangedAsync(tenantId, CalendarEventEmailKinds.Removed,
                CalendarEventIcs.For(existing.Id, existing.EndDate, existing.RecurrenceRule, DateTimeOffset.UtcNow),
                existing.Title, existing.StartDate, existing.Location, null, [participant.EmployeeId], [], organizerName, ct);
            await notifications.QueueIncidentEvaluationAsync(tenantId, [participant.EmployeeId], ct);
        }
        await unitOfWork.SaveChangesAsync(ct);
        return Result.Success();
    }
}
