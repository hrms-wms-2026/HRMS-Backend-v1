namespace ONEVO.Application.Features.Monitoring.TrayActivation.ServiceInterfaces;

/// <summary>
/// Server → one employee's tray "something changed, fetch it now" signal over the agent hub.
/// Replaces the tray's fast polling: the tray only polls slowly as a fallback while it has a
/// live push connection. A failed push must never fail the operation that triggered it — the
/// tray's fallback poll picks the change up later.
/// </summary>
public interface ITrayPushNotifier
{
    /// <summary>Call only after the change is committed, or the tray may re-read stale data.</summary>
    Task NotifyUserAsync(Guid tenantId, Guid userId, string message, CancellationToken ct);
}

/// <summary>Hub message names — mirrored by the tray's AgentCommandListener.</summary>
public static class TrayPushMessages
{
    /// <summary>Sent to a tray right after it connects: this backend pushes, so it may poll slowly.</summary>
    public const string PushReady = "PushReady";

    /// <summary>Clock-in/out or break start/end — re-read attendance status.</summary>
    public const string AttendanceChanged = "AttendanceChanged";

    /// <summary>A tray-visible wellness notification was created — fetch pending notifications.</summary>
    public const string NotificationsPending = "NotificationsPending";
}
