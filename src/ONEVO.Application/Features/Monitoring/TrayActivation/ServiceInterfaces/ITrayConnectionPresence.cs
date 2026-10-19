namespace ONEVO.Application.Features.Monitoring.TrayActivation.ServiceInterfaces;

/// <summary>
/// Whether the user currently has a tray holding a live agent-hub connection. A live connection
/// is proof of presence on its own, so the tray no longer has to heartbeat every 30 s while
/// connected; <c>LastSeenAt</c> (from the now-slow heartbeat) remains the fallback. In-memory,
/// so it assumes a single API instance — the agent hub has no backplane either.
/// </summary>
public interface ITrayConnectionPresence
{
    bool IsConnected(Guid tenantId, Guid userId);
}
