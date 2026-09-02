using MediatR;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Common.RepositoryInterfaces;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.Calendar.RepositoryInterfaces;
using ONEVO.Application.Features.Calendar.Services;

namespace ONEVO.Application.Features.Calendar.Commands.DeleteCalendarEvent;

public sealed class DeleteCalendarEventCommandHandler(
    ICurrentUser currentUser,
    ICalendarEventRepository events,
    ONEVO.Application.Features.CoreHr.Employee.RepositoryInterfaces.IEmployeeRepository employees,
    ICalendarNotificationSender notifications,
    IUnitOfWork unitOfWork)
    : IRequestHandler<DeleteCalendarEventCommand, Result>
{
    public async Task<Result> Handle(DeleteCalendarEventCommand request, CancellationToken ct)
    {
        if (!currentUser.IsAuthenticated)
            return Result.Forbidden();

        var tenantId = currentUser.TenantId;
        var existing = await events.GetTrackedByIdForTenantAsync(tenantId, request.Id, ct);
        if (existing is null)
            return Result.NotFound("Calendar event not found.");

        if (existing.CreatedById != currentUser.UserId)
            return Result.Forbidden("Only the event creator can delete this event.");

        var participantsByEvent = await events.GetParticipantsForEventsAsync(tenantId, [existing.Id], ct);
        var participantEmployeeIds = participantsByEvent.TryGetValue(existing.Id, out var p) ? p.Select(x => x.EmployeeId).ToList() : [];
        if (participantEmployeeIds.Count > 0)
        {
            var callerEmployee = await employees.GetDefaultForUserAsync(tenantId, currentUser.UserId, ct);
            var organizerName = callerEmployee is null ? "Someone" : $"{callerEmployee.FirstName} {callerEmployee.LastName}";
            await notifications.NotifyEventCancelledAsync(tenantId, existing.Title, participantEmployeeIds, organizerName, ct);
        }

        events.Remove(existing);
        await unitOfWork.SaveChangesAsync(ct);
        return Result.Success();
    }
}
