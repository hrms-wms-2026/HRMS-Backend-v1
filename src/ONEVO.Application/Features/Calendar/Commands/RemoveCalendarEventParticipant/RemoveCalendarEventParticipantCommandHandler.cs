using MediatR;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Common.RepositoryInterfaces;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.Calendar.RepositoryInterfaces;

namespace ONEVO.Application.Features.Calendar.Commands.RemoveCalendarEventParticipant;

public sealed record RemoveCalendarEventParticipantCommand(Guid EventId, Guid EmployeeId) : IRequest<Result>;

/// <summary>Removes one participant from an event that already exists. Organizer-only,
/// matching AddCalendarEventParticipantsCommandHandler. No email is sent for a removal - the
/// removed person still sees the event disappear from their own calendar next load.</summary>
public sealed class RemoveCalendarEventParticipantCommandHandler(
    ICurrentUser currentUser,
    ICalendarEventRepository events,
    IUnitOfWork unitOfWork)
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
        await unitOfWork.SaveChangesAsync(ct);
        return Result.Success();
    }
}
