using MediatR;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Features.Monitoring.CheckIn.ServiceInterfaces;
using ONEVO.Application.Features.Monitoring.Notifications.DTOs.Responses;
using ONEVO.Application.Features.Monitoring.Notifications.RepositoryInterfaces;

namespace ONEVO.Application.Features.Monitoring.Notifications.Queries.GetPendingTrayNotifications;

public class GetPendingTrayNotificationsQueryHandler
    : IRequestHandler<GetPendingTrayNotificationsQuery, Result<List<TrayNotificationDto>>>
{
    private readonly INotificationRepository _notifications;
    private readonly ITrayCurrentDevice _device;
    private readonly ITrayEmployeeIdentityResolver _employeeIdentity;

    public GetPendingTrayNotificationsQueryHandler(
        INotificationRepository notifications, ITrayCurrentDevice device, ITrayEmployeeIdentityResolver employeeIdentity)
    {
        _notifications = notifications;
        _device = device;
        _employeeIdentity = employeeIdentity;
    }

    public async Task<Result<List<TrayNotificationDto>>> Handle(GetPendingTrayNotificationsQuery request, CancellationToken ct)
    {
        if (!_device.IsAuthenticated || _device.TenantId == Guid.Empty || _device.UserId == Guid.Empty)
            return Result<List<TrayNotificationDto>>.Failure("A valid tray device token is required.", 401);

        // Must match the real Employee.Id the alert-evaluation jobs resolve and store on
        // Notification.EmployeeId - see ITrayEmployeeIdentityResolver's own doc comment.
        var employeeId = await _employeeIdentity.ResolveEmployeeIdAsync(
            _device.TenantId, _device.UserId, _device.LegalEntityId, ct);

        var pending = await _notifications.GetPendingForTrayAsync(_device.TenantId, employeeId, ct);

        return Result<List<TrayNotificationDto>>.Success(
            pending.Select(n => new TrayNotificationDto(n.Id, n.Type.ToString(), n.Title, n.Message)).ToList());
    }
}
