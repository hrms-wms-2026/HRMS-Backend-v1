using MediatR;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.DevPlatform.Tenancy.RepositoryInterfaces;
using ONEVO.Application.Features.Monitoring.CheckIn.ServiceInterfaces;
using ONEVO.Application.Features.SharedPlatform.Notifications.RepositoryInterfaces;

namespace ONEVO.Application.Features.SharedPlatform.Notifications.Tray.Queries.GetPendingTrayDeliveries;

public sealed class GetPendingTrayDeliveriesQueryHandler
    : IRequestHandler<GetPendingTrayDeliveriesQuery, Result<IReadOnlyList<TrayDeliveryDto>>>
{
    private const int MaxPerPoll = 50;

    private readonly INotificationDeliveryRepository _deliveries;
    private readonly ITrayCurrentDevice _device;
    private readonly ITenantRepository _tenants;
    private readonly ITenantContextSwitcher _switcher;
    private readonly IDateTimeProvider _clock;

    public GetPendingTrayDeliveriesQueryHandler(
        INotificationDeliveryRepository deliveries, ITrayCurrentDevice device,
        ITenantRepository tenants, ITenantContextSwitcher switcher, IDateTimeProvider clock)
    {
        _deliveries = deliveries;
        _device = device;
        _tenants = tenants;
        _switcher = switcher;
        _clock = clock;
    }

    public async Task<Result<IReadOnlyList<TrayDeliveryDto>>> Handle(GetPendingTrayDeliveriesQuery request, CancellationToken ct)
    {
        if (!await TrayDeliveryDevice.EnterDeviceTenantAsync(_device, _tenants, _switcher, ct))
            return Result<IReadOnlyList<TrayDeliveryDto>>.Failure("A valid tray device token is required.", 401);

        // Recipients are users, and the device token is bound to one user in one tenant - no client-supplied ids.
        var pending = await _deliveries.ListPendingTrayAsync(_device.TenantId, _device.UserId, _clock.UtcNow, MaxPerPoll, ct);
        IReadOnlyList<TrayDeliveryDto> items = pending
            .Select(d => new TrayDeliveryDto(d.Id, d.EventKey, d.Title, d.Body, d.ActionKey, d.RelatedEntityType, d.RelatedEntityId, d.ExpiresAt))
            .ToList();
        return Result<IReadOnlyList<TrayDeliveryDto>>.Success(items);
    }
}
