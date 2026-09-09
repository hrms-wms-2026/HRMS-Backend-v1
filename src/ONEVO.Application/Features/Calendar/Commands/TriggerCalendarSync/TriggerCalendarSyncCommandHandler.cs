using MediatR;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.Calendar.RepositoryInterfaces;
using ONEVO.Application.Features.Calendar.ServiceInterfaces;

namespace ONEVO.Application.Features.Calendar.Commands.TriggerCalendarSync;

public sealed class TriggerCalendarSyncCommandHandler(
    ICurrentUser currentUser,
    IExternalCalendarConnectionRepository connections,
    ICalendarSyncService syncService)
    : IRequestHandler<TriggerCalendarSyncCommand, Result<Unit>>
{
    public async Task<Result<Unit>> Handle(TriggerCalendarSyncCommand request, CancellationToken ct)
    {
        if (!currentUser.IsAuthenticated)
            return Result<Unit>.Forbidden();

        var tenantId = currentUser.TenantId;
        var existing = await connections.GetByIdForTenantAsync(tenantId, request.Id, ct);
        if (existing is null)
            return Result<Unit>.NotFound("Calendar connection not found.");

        if (existing.UserId != currentUser.UserId)
            return Result<Unit>.Forbidden("Only the connection owner can trigger a sync.");

        await syncService.SyncConnectionAsync(tenantId, existing.Id, ct);
        return Result<Unit>.Success(Unit.Value);
    }
}
