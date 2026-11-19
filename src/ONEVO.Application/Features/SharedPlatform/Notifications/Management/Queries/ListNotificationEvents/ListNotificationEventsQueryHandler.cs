using MediatR;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Features.SharedPlatform.Notifications.Catalog;
using ONEVO.Application.Features.SharedPlatform.Notifications.Management.DTOs;
using ONEVO.Application.Features.SharedPlatform.Notifications.Management.Mappers;

namespace ONEVO.Application.Features.SharedPlatform.Notifications.Management.Queries.ListNotificationEvents;

public sealed class ListNotificationEventsQueryHandler
    : IRequestHandler<ListNotificationEventsQuery, Result<IReadOnlyList<NotificationEventResponse>>>
{
    public Task<Result<IReadOnlyList<NotificationEventResponse>>> Handle(ListNotificationEventsQuery request, CancellationToken ct)
    {
        IReadOnlyList<NotificationEventResponse> events = NotificationEventCatalog.All
            .Where(e => string.IsNullOrWhiteSpace(request.Module) || e.Module == request.Module)
            .OrderBy(e => e.Module).ThenBy(e => e.DisplayName)
            .Select(e => e.ToResponse())
            .ToList();
        return Task.FromResult(Result<IReadOnlyList<NotificationEventResponse>>.Success(events));
    }
}
