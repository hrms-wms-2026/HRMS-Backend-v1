using MediatR;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.Monitoring.Notifications.DTOs.Responses;
using ONEVO.Application.Features.Monitoring.Notifications.RepositoryInterfaces;

namespace ONEVO.Application.Features.Monitoring.Notifications.Queries.GetNotificationInbox;

public class GetNotificationInboxQueryHandler
    : IRequestHandler<GetNotificationInboxQuery, Result<PagedResult<NotificationInboxItemDto>>>
{
    private readonly INotificationRepository _notifications;
    private readonly ICurrentUser _currentUser;

    public GetNotificationInboxQueryHandler(INotificationRepository notifications, ICurrentUser currentUser)
    {
        _notifications = notifications;
        _currentUser = currentUser;
    }

    public async Task<Result<PagedResult<NotificationInboxItemDto>>> Handle(
        GetNotificationInboxQuery request, CancellationToken ct)
    {
        if (!_currentUser.IsAuthenticated || _currentUser.TenantId == Guid.Empty)
            return Result<PagedResult<NotificationInboxItemDto>>.Forbidden("Authentication required.");

        var page = request.Page < 1 ? 1 : request.Page;
        var pageSize = request.PageSize is < 1 or > 100 ? 20 : request.PageSize;
        var tenantId = _currentUser.TenantId;
        var userId = _currentUser.UserId;

        var total = await _notifications.GetInboxTotalCountAsync(tenantId, userId, ct);
        var items = await _notifications.GetInboxAsync(tenantId, userId, page, pageSize, ct);

        var dtos = items.Select(n => new NotificationInboxItemDto(
            n.Id, n.Type.ToString(), n.Title, n.Message, n.CreatedAt, n.ReadAt)).ToList();

        return Result<PagedResult<NotificationInboxItemDto>>.Success(
            new PagedResult<NotificationInboxItemDto>(dtos, page, pageSize, total));
    }
}
