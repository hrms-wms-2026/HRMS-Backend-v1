using MediatR;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Common.RepositoryInterfaces;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.Calendar.RepositoryInterfaces;
using ONEVO.Domain.Features.Calendar.Entities;

namespace ONEVO.Application.Features.Calendar.Commands.RespondToCalendarEvent;

public sealed class RespondToCalendarEventCommandHandler(
    ICurrentUser currentUser,
    ICalendarEventRepository events,
    ONEVO.Application.Features.CoreHr.Employee.RepositoryInterfaces.IEmployeeRepository employees,
    IUnitOfWork unitOfWork)
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
            _ => null
        };
        if (normalizedStatus is null)
            return Result.Failure("ResponseStatus must be 'Accepted' or 'Rejected'.", 400);

        var tenantId = currentUser.TenantId;
        var employee = await employees.GetDefaultForUserAsync(tenantId, currentUser.UserId, ct);
        if (employee is null)
            return Result.Forbidden("No employee record for the current user.");

        var participant = await events.GetTrackedParticipantAsync(tenantId, request.EventId, employee.Id, ct);
        if (participant is null)
            return Result.NotFound("You are not a participant on this event.");

        participant.ResponseStatus = normalizedStatus;
        await unitOfWork.SaveChangesAsync(ct);

        return Result.Success();
    }
}
