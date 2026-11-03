using MediatR;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Common.RepositoryInterfaces;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.DevPlatform.Tenancy.RepositoryInterfaces;
using ONEVO.Application.Features.Monitoring.CheckIn.ServiceInterfaces;
using ONEVO.Application.Features.SharedPlatform.Notifications.RepositoryInterfaces;
using ONEVO.Domain.Features.SharedPlatform.Notifications.Entities;

namespace ONEVO.Application.Features.SharedPlatform.Notifications.Tray.Commands.AckTrayDelivery;

public sealed class AckTrayDeliveryCommandHandler : IRequestHandler<AckTrayDeliveryCommand, Result>
{
    private readonly INotificationDeliveryRepository _deliveries;
    private readonly ITrayCurrentDevice _device;
    private readonly ITenantRepository _tenants;
    private readonly ITenantContextSwitcher _switcher;
    private readonly IUnitOfWork _unitOfWork;
    private readonly IDateTimeProvider _clock;

    public AckTrayDeliveryCommandHandler(
        INotificationDeliveryRepository deliveries, ITrayCurrentDevice device, ITenantRepository tenants,
        ITenantContextSwitcher switcher, IUnitOfWork unitOfWork, IDateTimeProvider clock)
    {
        _deliveries = deliveries;
        _device = device;
        _tenants = tenants;
        _switcher = switcher;
        _unitOfWork = unitOfWork;
        _clock = clock;
    }

    public async Task<Result> Handle(AckTrayDeliveryCommand request, CancellationToken ct)
    {
        if (!await TrayDeliveryDevice.EnterDeviceTenantAsync(_device, _tenants, _switcher, ct))
            return Result.Failure("A valid tray device token is required.", 401);

        var delivery = await _deliveries.GetTrackedAsync(request.DeliveryId, ct);
        // Another user's or tenant's delivery is indistinguishable from a missing one.
        if (delivery is null || delivery.TenantId != _device.TenantId || delivery.RecipientUserId != _device.UserId
            || delivery.Channel != NotificationChannels.Tray)
            return Result.NotFound("Notification not found.");

        if (delivery.Status == NotificationDeliveryStatus.ClientAccepted)
            return Result.Success(); // repeated ack after a retry - already recorded

        var now = _clock.UtcNow;
        if (delivery.Status != NotificationDeliveryStatus.Pending)
            return Result.Conflict("This notification is no longer pending.");

        if (delivery.ExpiresAt is { } expiresAt && expiresAt <= now)
        {
            delivery.Status = NotificationDeliveryStatus.Expired;
            delivery.StatusReason = "acknowledged_after_expiry";
            delivery.UpdatedAt = now;
            delivery.CompletedAt = now;
            await _unitOfWork.SaveChangesAsync(ct);
            return Result.Conflict("This notification expired before the tray accepted it.");
        }

        delivery.Status = NotificationDeliveryStatus.ClientAccepted;
        delivery.StatusReason = null;
        delivery.AttemptCount++;
        delivery.UpdatedAt = now;
        delivery.CompletedAt = now;
        await _unitOfWork.SaveChangesAsync(ct);
        return Result.Success();
    }
}
