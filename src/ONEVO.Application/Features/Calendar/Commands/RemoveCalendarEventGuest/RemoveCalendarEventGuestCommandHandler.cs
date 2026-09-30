using MediatR;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Common.RepositoryInterfaces;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.Calendar.Helpers;
using ONEVO.Application.Features.Calendar.RepositoryInterfaces;

namespace ONEVO.Application.Features.Calendar.Commands.RemoveCalendarEventGuest;

public sealed record RemoveCalendarEventGuestCommand(Guid EventId, string Email) : IRequest<Result>;

/// <summary>Removes one external guest from an event. Organizer-only. The row is really deleted
/// (guests have no soft-delete), so the same address can be invited again later.</summary>
public sealed class RemoveCalendarEventGuestCommandHandler(
    ICurrentUser currentUser,
    ICalendarEventRepository events,
    IUnitOfWork unitOfWork)
    : IRequestHandler<RemoveCalendarEventGuestCommand, Result>
{
    public async Task<Result> Handle(RemoveCalendarEventGuestCommand request, CancellationToken ct)
    {
        if (!currentUser.IsAuthenticated)
            return Result.Forbidden();

        if (!CalendarEventValidation.TryNormalizeGuestEmail(request.Email, out var email))
            return Result.Failure($"'{request.Email}' is not a valid email address.", 400);

        var tenantId = currentUser.TenantId;
        var existing = await events.GetTrackedByIdForTenantAsync(tenantId, request.EventId, ct);
        if (existing is null)
            return Result.NotFound("Calendar event not found.");

        if (existing.CreatedById != currentUser.UserId)
            return Result.Forbidden("Only the event organizer can remove guests.");

        var guest = await events.GetTrackedGuestAsync(tenantId, request.EventId, email, ct);
        if (guest is null)
            return Result.NotFound("This guest is not invited to this event.");

        events.RemoveGuest(guest);
        await unitOfWork.SaveChangesAsync(ct);
        return Result.Success();
    }
}
