using MediatR;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Common.RepositoryInterfaces;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.Calendar.DTOs.Responses;
using ONEVO.Application.Features.Calendar.RepositoryInterfaces;
using ONEVO.Application.Features.Calendar.Services;
using ONEVO.Domain.Features.Calendar.Entities;

namespace ONEVO.Application.Features.Calendar.Commands.AddCalendarEventParticipants;

public sealed record AddCalendarEventParticipantsCommand(Guid EventId, IReadOnlyList<Guid> EmployeeIds)
    : IRequest<Result<CalendarEventParticipantsResult>>;

public sealed record CalendarEventParticipantsResult(IReadOnlyList<CalendarEventParticipantSummary> Participants);

/// <summary>Adds participants to an event that already exists - the create-time flow
/// (CreateCalendarEventCommandHandler) covers participants picked before the event is saved,
/// this covers picking more afterward. Organizer-only, matching every other event-mutation
/// handler's authorization rule.</summary>
public sealed class AddCalendarEventParticipantsCommandHandler(
    ICurrentUser currentUser,
    ICalendarEventRepository events,
    ONEVO.Application.Features.CoreHr.Employee.RepositoryInterfaces.IEmployeeRepository employees,
    ICalendarNotificationSender notifications,
    IUnitOfWork unitOfWork)
    : IRequestHandler<AddCalendarEventParticipantsCommand, Result<CalendarEventParticipantsResult>>
{
    public async Task<Result<CalendarEventParticipantsResult>> Handle(
        AddCalendarEventParticipantsCommand request, CancellationToken ct)
    {
        if (!currentUser.IsAuthenticated)
            return Result<CalendarEventParticipantsResult>.Forbidden();

        var tenantId = currentUser.TenantId;
        var existing = await events.GetTrackedByIdForTenantAsync(tenantId, request.EventId, ct);
        if (existing is null)
            return Result<CalendarEventParticipantsResult>.NotFound("Calendar event not found.");

        if (existing.CreatedById != currentUser.UserId)
            return Result<CalendarEventParticipantsResult>.Forbidden("Only the event organizer can add participants.");

        return await unitOfWork.ExecuteInTransactionAsync(async innerCt =>
        {
            var participantsByEvent = await events.GetParticipantsForEventsAsync(tenantId, [existing.Id], innerCt);
            var currentParticipants = participantsByEvent.TryGetValue(existing.Id, out var p)
                ? p.ToList() : new List<CalendarEventParticipant>();
            var existingEmployeeIds = currentParticipants.Select(x => x.EmployeeId).ToHashSet();

            var newEmployeeIds = request.EmployeeIds.Distinct().Where(id => !existingEmployeeIds.Contains(id)).ToList();
            if (newEmployeeIds.Count > 0)
            {
                var removedRows = await events.GetRemovedParticipantsAsync(tenantId, existing.Id, newEmployeeIds, innerCt);
                foreach (var row in removedRows)
                {
                    row.IsDeleted = false;
                    row.DeletedAt = null;
                    row.ResponseStatus = CalendarEventParticipantStatuses.Pending;
                    row.ResponseReason = null;
                }
                currentParticipants.AddRange(removedRows);

                var revivedEmployeeIds = removedRows.Select(r => r.EmployeeId).ToHashSet();
                var newParticipants = newEmployeeIds.Where(id => !revivedEmployeeIds.Contains(id)).Select(employeeId => new CalendarEventParticipant
                {
                    Id = Guid.NewGuid(), TenantId = tenantId, EventId = existing.Id, EmployeeId = employeeId,
                    ResponseStatus = CalendarEventParticipantStatuses.Pending
                }).ToList();
                if (newParticipants.Count > 0)
                    await events.AddParticipantsAsync(newParticipants, innerCt);
                currentParticipants.AddRange(newParticipants);

                var callerEmployee = await employees.GetDefaultForUserAsync(tenantId, currentUser.UserId, innerCt);
                var organizerName = callerEmployee is null ? "Someone" : $"{callerEmployee.FirstName} {callerEmployee.LastName}";
                await notifications.NotifyParticipantsAddedAsync(
                    tenantId, existing.Title, existing.StartDate, existing.Location,
                    newEmployeeIds, organizerName, innerCt, existing.MeetingLink);

                await unitOfWork.SaveChangesAsync(innerCt);
            }

            var summaries = new List<CalendarEventParticipantSummary>();
            foreach (var participant in currentParticipants)
            {
                var employee = await employees.GetByIdAsync(tenantId, participant.EmployeeId, innerCt);
                summaries.Add(new CalendarEventParticipantSummary(
                    participant.EmployeeId, employee is null ? "Unknown" : $"{employee.FirstName} {employee.LastName}",
                    participant.ResponseStatus));
            }

            return Result<CalendarEventParticipantsResult>.Success(new CalendarEventParticipantsResult(summaries));
        }, ct);
    }
}
