using System.Text.Json;
using Microsoft.Extensions.Logging;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.Monitoring.TrayActivation.ServiceInterfaces;
using ONEVO.Application.Features.SharedPlatform.Notifications.Delivery;

namespace ONEVO.Application.Features.SharedPlatform.Notifications.Tray;

/// <summary>
/// Wakes the recipient's tray right after a tray delivery is committed - the same NotificationsPending signal
/// the legacy wellness alerts use - so it fetches now instead of on its slow fallback poll. Best effort: a
/// failed or unavailable push never fails the message; the fallback poll still delivers.
/// </summary>
public sealed class NotificationTrayPushOutboxHandler : IOutboxMessageHandler
{
    private readonly ILogger<NotificationTrayPushOutboxHandler> _logger;
    private readonly ITrayPushNotifier? _push;

    public NotificationTrayPushOutboxHandler(ILogger<NotificationTrayPushOutboxHandler> logger, ITrayPushNotifier? push = null)
    {
        _logger = logger;
        _push = push;
    }

    public string Type => OutboxMessageTypes.NotificationTrayPush;

    public async Task HandleAsync(string payloadJson, CancellationToken ct)
    {
        var payload = JsonSerializer.Deserialize<NotificationTrayPushPayload>(payloadJson);
        if (payload is null || _push is null)
            return;

        try
        {
            await _push.NotifyUserAsync(payload.TenantId, payload.UserId, TrayPushMessages.NotificationsPending, ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogDebug(ex, "Tray wake-up for user {UserId} failed; the tray fallback poll will deliver.", payload.UserId);
        }
    }
}
