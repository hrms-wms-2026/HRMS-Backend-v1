using MediatR;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.Monitoring.CheckIn.ServiceInterfaces;
using ONEVO.Application.Features.Monitoring.Notifications.RepositoryInterfaces;

namespace ONEVO.Application.Features.Monitoring.Notifications.Commands.AckTrayNotification;

public class AckTrayNotificationCommandHandler : IRequestHandler<AckTrayNotificationCommand, Result>
{
    private readonly INotificationRepository _notifications;
    private readonly ITrayCurrentDevice _device;
    private readonly IDateTimeProvider _clock;

    public AckTrayNotificationCommandHandler(
        INotificationRepository notifications, ITrayCurrentDevice device, IDateTimeProvider clock)
    {
        _notifications = notifications;
        _device = device;
        _clock = clock;
    }

    public async Task<Result> Handle(AckTrayNotificationCommand request, CancellationToken ct)
    {
        if (!_device.IsAuthenticated || _device.TenantId == Guid.Empty)
            return Result.Failure("A valid tray device token is required.", 401);

        var notification = await _notifications.GetByIdAsync(_device.TenantId, request.NotificationId, ct);
        if (notification is null || notification.EmployeeId != _device.UserId)
            return Result.NotFound("Notification not found.");

        notification.DeliveredToTrayAt = _clock.UtcNow;
        _notifications.Update(notification);
        await _notifications.SaveChangesAsync(ct);

        return Result.Success();
    }
}
