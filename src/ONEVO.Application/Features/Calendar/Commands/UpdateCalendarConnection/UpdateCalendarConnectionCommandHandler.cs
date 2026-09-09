using MediatR;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Common.RepositoryInterfaces;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.Calendar.DTOs.Responses;
using ONEVO.Application.Features.Calendar.RepositoryInterfaces;
using ONEVO.Domain.Features.Calendar.Entities;

namespace ONEVO.Application.Features.Calendar.Commands.UpdateCalendarConnection;

public sealed class UpdateCalendarConnectionCommandHandler(
    ICurrentUser currentUser,
    IExternalCalendarConnectionRepository connections,
    IUnitOfWork unitOfWork)
    : IRequestHandler<UpdateCalendarConnectionCommand, Result<CalendarConnectionItem>>
{
    private static readonly string[] ValidDirections =
        [CalendarSyncDirections.PullOnly, CalendarSyncDirections.PushOnly, CalendarSyncDirections.TwoWay, CalendarSyncDirections.Disabled];

    public async Task<Result<CalendarConnectionItem>> Handle(UpdateCalendarConnectionCommand request, CancellationToken ct)
    {
        if (!currentUser.IsAuthenticated)
            return Result<CalendarConnectionItem>.Forbidden();

        if (!ValidDirections.Contains(request.SyncDirection))
            return Result<CalendarConnectionItem>.Failure("Invalid sync direction.", 400);

        var existing = await connections.GetTrackedByIdForTenantAsync(currentUser.TenantId, request.Id, ct);
        if (existing is null)
            return Result<CalendarConnectionItem>.NotFound("Calendar connection not found.");

        if (existing.UserId != currentUser.UserId)
            return Result<CalendarConnectionItem>.Forbidden("Only the connection owner can change its sync mode.");

        return await unitOfWork.ExecuteInTransactionAsync(async innerCt =>
        {
            existing.SyncDirection = request.SyncDirection;
            existing.UpdatedAt = DateTimeOffset.UtcNow;
            connections.Update(existing);
            await unitOfWork.SaveChangesAsync(innerCt);

            return Result<CalendarConnectionItem>.Success(new CalendarConnectionItem(
                existing.Id, existing.Provider, existing.ExternalAccountEmail, existing.ExternalCalendarName,
                existing.SyncDirection, existing.Status, existing.LastSyncedAt, existing.LastError));
        }, ct);
    }
}
