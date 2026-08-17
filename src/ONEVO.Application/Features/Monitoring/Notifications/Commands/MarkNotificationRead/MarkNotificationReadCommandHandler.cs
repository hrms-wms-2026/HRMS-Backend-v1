using MediatR;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.Monitoring.Notifications.RepositoryInterfaces;

namespace ONEVO.Application.Features.Monitoring.Notifications.Commands.MarkNotificationRead;

public class MarkNotificationReadCommandHandler : IRequestHandler<MarkNotificationReadCommand, Result>
{
    private readonly INotificationRepository _notifications;
    private readonly ICurrentUser _currentUser;
    private readonly IDateTimeProvider _clock;

    public MarkNotificationReadCommandHandler(
        INotificationRepository notifications, ICurrentUser currentUser, IDateTimeProvider clock)
    {
        _notifications = notifications;
        _currentUser = currentUser;
        _clock = clock;
    }

    public async Task<Result> Handle(MarkNotificationReadCommand request, CancellationToken ct)
    {
        if (!_currentUser.IsAuthenticated || _currentUser.TenantId == Guid.Empty)
            return Result.Forbidden("Authentication required.");

        var notification = await _notifications.GetByIdAsync(_currentUser.TenantId, request.NotificationId, ct);
        if (notification is null || notification.EmployeeId != _currentUser.UserId)
            return Result.NotFound("Notification not found.");

        notification.ReadAt = _clock.UtcNow;
        _notifications.Update(notification);
        await _notifications.SaveChangesAsync(ct);

        return Result.Success();
    }
}
