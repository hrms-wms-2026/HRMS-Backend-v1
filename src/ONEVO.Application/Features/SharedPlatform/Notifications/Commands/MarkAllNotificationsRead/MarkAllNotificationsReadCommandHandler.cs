using MediatR;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Common.RepositoryInterfaces;
using ONEVO.Application.Common.ServiceInterfaces;

namespace ONEVO.Application.Features.SharedPlatform.Notifications.Commands.MarkAllNotificationsRead;

public class MarkAllNotificationsReadCommandHandler : IRequestHandler<MarkAllNotificationsReadCommand, Result>
{
    private readonly ICurrentUser _currentUser;
    private readonly INotificationRepository _notifications;

    public MarkAllNotificationsReadCommandHandler(ICurrentUser currentUser, INotificationRepository notifications)
    {
        _currentUser = currentUser;
        _notifications = notifications;
    }

    public async Task<Result> Handle(MarkAllNotificationsReadCommand request, CancellationToken ct)
    {
        if (!_currentUser.IsAuthenticated)
            return Result.Forbidden("Authentication required.");

        await _notifications.MarkAllReadAsync(_currentUser.TenantId, _currentUser.UserId, ct);
        return Result.Success();
    }
}
