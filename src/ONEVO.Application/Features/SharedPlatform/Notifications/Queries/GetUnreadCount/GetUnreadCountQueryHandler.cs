using MediatR;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Common.RepositoryInterfaces;
using ONEVO.Application.Common.ServiceInterfaces;

namespace ONEVO.Application.Features.SharedPlatform.Notifications.Queries.GetUnreadCount;

public class GetUnreadCountQueryHandler : IRequestHandler<GetUnreadCountQuery, Result<int>>
{
    private readonly ICurrentUser _currentUser;
    private readonly INotificationRepository _notifications;

    public GetUnreadCountQueryHandler(ICurrentUser currentUser, INotificationRepository notifications)
    {
        _currentUser = currentUser;
        _notifications = notifications;
    }

    public async Task<Result<int>> Handle(GetUnreadCountQuery request, CancellationToken ct)
    {
        if (!_currentUser.IsAuthenticated)
            return Result<int>.Forbidden("Authentication required.");

        var count = await _notifications.GetUnreadCountAsync(_currentUser.TenantId, _currentUser.UserId, ct);
        return Result<int>.Success(count);
    }
}
