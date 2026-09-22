using MediatR;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.Calendar.DTOs.Responses;
using ONEVO.Application.Features.Calendar.RepositoryInterfaces;

namespace ONEVO.Application.Features.Calendar.Queries.GetMyCalendarConnections;

public sealed class GetMyCalendarConnectionsQueryHandler(
    ICurrentUser currentUser,
    IExternalCalendarConnectionRepository connections)
    : IRequestHandler<GetMyCalendarConnectionsQuery, Result<CalendarConnectionsResponse>>
{
    public async Task<Result<CalendarConnectionsResponse>> Handle(GetMyCalendarConnectionsQuery request, CancellationToken ct)
    {
        if (!currentUser.IsAuthenticated)
            return Result<CalendarConnectionsResponse>.Forbidden();

        var rows = await connections.GetForUserAsync(currentUser.TenantId, currentUser.UserId, ct);
        var items = rows.Select(c => new CalendarConnectionItem(
            c.Id, c.Provider, c.ExternalAccountEmail, c.ExternalCalendarName, c.SyncDirection, c.Status, c.LastSyncedAt, c.LastError)).ToList();

        return Result<CalendarConnectionsResponse>.Success(new CalendarConnectionsResponse(items));
    }
}
