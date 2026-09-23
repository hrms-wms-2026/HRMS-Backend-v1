using MediatR;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Common.RepositoryInterfaces;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.Calendar.RepositoryInterfaces;
using ONEVO.Domain.Features.Calendar.Entities;

namespace ONEVO.Application.Features.Calendar.Commands.DisconnectCalendarConnection;

public sealed class DisconnectCalendarConnectionCommandHandler(
    ICurrentUser currentUser,
    IExternalCalendarConnectionRepository connections,
    IExternalCalendarEventLinkRepository links,
    ICalendarEventRepository events,
    IUnitOfWork unitOfWork)
    : IRequestHandler<DisconnectCalendarConnectionCommand, Result<Unit>>
{
    public async Task<Result<Unit>> Handle(DisconnectCalendarConnectionCommand request, CancellationToken ct)
    {
        if (!currentUser.IsAuthenticated)
            return Result<Unit>.Forbidden();

        var tenantId = currentUser.TenantId;
        var existing = await connections.GetTrackedByIdForTenantAsync(tenantId, request.Id, ct);
        if (existing is null)
            return Result<Unit>.NotFound("Calendar connection not found.");

        if (existing.UserId != currentUser.UserId)
            return Result<Unit>.Forbidden("Only the connection owner can disconnect it.");

        return await unitOfWork.ExecuteInTransactionAsync(async innerCt =>
        {
            var connectionLinks = await links.GetByConnectionIdAsync(tenantId, existing.Id, innerCt);

            // Only delete events still owned by the sync (SourceType == ExternalSync). An event that
            // was later "converted" to a OneVo-owned event keeps existing after disconnect - there is
            // no convert command yet in this codebase, so this branch is presently unreachable, but the
            // check stays correct for when a later spec adds one (per the design spec's explicit note).
            foreach (var link in connectionLinks)
            {
                var linkedEvent = await events.GetTrackedByIdForTenantAsync(tenantId, link.CalendarEventId, innerCt);
                if (linkedEvent is not null && linkedEvent.SourceType == CalendarEventSourceTypes.ExternalSync)
                    events.Remove(linkedEvent);
            }

            links.RemoveRange(connectionLinks);
            connections.Remove(existing);
            await unitOfWork.SaveChangesAsync(innerCt);
            return Result<Unit>.Success(Unit.Value);
        }, ct);
    }
}
