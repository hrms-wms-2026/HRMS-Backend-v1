using MediatR;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Common.RepositoryInterfaces;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.Calendar.RepositoryInterfaces;
using ONEVO.Application.Features.Calendar.Services;
using ONEVO.Domain.Features.Calendar.Entities;

namespace ONEVO.Application.Features.Calendar.Commands.RespondToCalendarEvent;

public sealed class RespondToCalendarEventCommandHandler(
    ICurrentUser currentUser,
    ICalendarEventRepository events,
    ONEVO.Application.Features.CoreHr.Employee.RepositoryInterfaces.IEmployeeRepository employees,
    IUnitOfWork unitOfWork,
    ICalendarNotificationSender notifications)
    : IRequestHandler<RespondToCalendarEventCommand, Result>
{
    public async Task<Result> Handle(RespondToCalendarEventCommand request, CancellationToken ct)
    {
        if (!currentUser.IsAuthenticated)
            return Result.Forbidden();

        var normalizedStatus = request.ResponseStatus switch
        {
            "Accepted" => CalendarEventParticipantStatuses.Accepted,
            "Rejected" => CalendarEventParticipantStatuses.Rejected,
            "ResolutionRequested" => CalendarEventParticipantStatuses.ResolutionRequested,
            "ReplacementNominated" => CalendarEventParticipantStatuses.ReplacementNominated,
            _ => null
        };
        if (normalizedStatus is null)
            return Result.Failure("ResponseStatus must be 'Accepted', 'Rejected', 'ResolutionRequested', or 'ReplacementNominated'.", 400);

        if (normalizedStatus == CalendarEventParticipantStatuses.ResolutionRequested && string.IsNullOrWhiteSpace(request.Reason))
            return Result.Failure("A reason is required to request conflict resolution.", 400);

        var tenantId = currentUser.TenantId;
        var employee = await employees.GetDefaultForUserAsync(tenantId, currentUser.UserId, ct);
        if (employee is null)
            return Result.Forbidden("No employee record for the current user.");

        var participant = await events.GetTrackedParticipantAsync(tenantId, request.EventId, employee.Id, ct);
        if (participant is null)
            return Result.NotFound("You are not a participant on this event.");

        string? nomineeName = null;
        if (normalizedStatus == CalendarEventParticipantStatuses.ReplacementNominated)
        {
            if (request.NomineeEmployeeId is not { } nomineeId)
                return Result.Failure("A nominee is required to nominate a replacement.", 400);

            var coParticipants = await events.GetParticipantsForEventsAsync(tenantId, [request.EventId], ct);
            var isEligible = coParticipants.TryGetValue(request.EventId, out var list)
                && list.Any(p => p.EmployeeId == nomineeId && p.EmployeeId != employee.Id);
            if (!isEligible)
                return Result.Failure("The nominee must be another participant on this event.", 400);

            var nominee = await employees.GetByIdAsync(tenantId, nomineeId, ct);
            nomineeName = nominee is null ? "Unknown" : $"{nominee.FirstName} {nominee.LastName}";
        }

        participant.ResponseStatus = normalizedStatus;
        participant.ResponseReason = request.Reason;
        await unitOfWork.SaveChangesAsync(ct);

        if (normalizedStatus is CalendarEventParticipantStatuses.ResolutionRequested or CalendarEventParticipantStatuses.ReplacementNominated)
        {
            var calendarEvent = await events.GetByIdForTenantAsync(tenantId, request.EventId, ct);
            if (calendarEvent is not null)
            {
                var responderName = $"{employee.FirstName} {employee.LastName}";
                if (normalizedStatus == CalendarEventParticipantStatuses.ResolutionRequested)
                    await notifications.NotifyResolutionRequestedAsync(tenantId, calendarEvent.CreatedById, calendarEvent.Title, responderName, request.Reason!, ct);
                else
                    await notifications.NotifyReplacementNominatedAsync(tenantId, calendarEvent.CreatedById, calendarEvent.Title, responderName, nomineeName!, ct);
            }
        }

        return Result.Success();
    }
}
