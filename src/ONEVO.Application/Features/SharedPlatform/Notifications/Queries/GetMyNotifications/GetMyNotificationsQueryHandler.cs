using MediatR;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Common.RepositoryInterfaces;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.SharedPlatform.Notifications.DTOs.Responses;

namespace ONEVO.Application.Features.SharedPlatform.Notifications.Queries.GetMyNotifications;

public class GetMyNotificationsQueryHandler : IRequestHandler<GetMyNotificationsQuery, Result<IReadOnlyList<NotificationResponse>>>
{
    private readonly ICurrentUser _currentUser;
    private readonly INotificationRepository _notifications;

    public GetMyNotificationsQueryHandler(ICurrentUser currentUser, INotificationRepository notifications)
    {
        _currentUser = currentUser;
        _notifications = notifications;
    }

    public async Task<Result<IReadOnlyList<NotificationResponse>>> Handle(GetMyNotificationsQuery request, CancellationToken ct)
    {
        if (!_currentUser.IsAuthenticated)
            return Result<IReadOnlyList<NotificationResponse>>.Forbidden("Authentication required.");

        var page = request.Page < 1 ? 1 : request.Page;
        var pageSize = request.PageSize is < 1 or > 100 ? 20 : request.PageSize;
        var items = await _notifications.GetByRecipientAsync(
            _currentUser.TenantId, _currentUser.UserId, request.UnreadOnly, page, pageSize, ct);

        var responses = items.Select(n => new NotificationResponse(
            n.Id, n.TemplateCode, n.Title, n.Body, n.RelatedEntityType, n.RelatedEntityId,
            n.IsRead, n.ReadAt, n.CreatedAt)).ToList();

        return Result<IReadOnlyList<NotificationResponse>>.Success(responses);
    }
}
