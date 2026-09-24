using MediatR;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Common.RepositoryInterfaces;
using ONEVO.Application.Common.ServiceInterfaces;

namespace ONEVO.Application.Features.SharedPlatform.Notifications.Commands.MarkNotificationRead;

public class MarkNotificationReadCommandHandler : IRequestHandler<MarkNotificationReadCommand, Result>
{
    private readonly ICurrentUser _currentUser;
    private readonly INotificationRepository _notifications;
    private readonly IUnitOfWork _unitOfWork;

    public MarkNotificationReadCommandHandler(
        ICurrentUser currentUser, INotificationRepository notifications, IUnitOfWork unitOfWork)
    {
        _currentUser = currentUser;
        _notifications = notifications;
        _unitOfWork = unitOfWork;
    }

    public async Task<Result> Handle(MarkNotificationReadCommand request, CancellationToken ct)
    {
        if (!_currentUser.IsAuthenticated)
            return Result.Forbidden("Authentication required.");

        var notification = await _notifications.GetTrackedByIdForRecipientAsync(
            _currentUser.TenantId, request.NotificationId, _currentUser.UserId, ct);
        if (notification is null)
            return Result.NotFound("Notification not found.");

        if (!notification.IsRead)
        {
            notification.IsRead = true;
            notification.ReadAt = DateTimeOffset.UtcNow;
            await _unitOfWork.SaveChangesAsync(ct);
        }

        return Result.Success();
    }
}
