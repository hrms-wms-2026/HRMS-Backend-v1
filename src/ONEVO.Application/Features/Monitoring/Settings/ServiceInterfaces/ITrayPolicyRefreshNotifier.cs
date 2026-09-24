namespace ONEVO.Application.Features.Monitoring.Settings.ServiceInterfaces;

/// <summary>
/// Tells connected tray agents to fetch monitoring policy again.
/// A failed notify must not fail the settings save.
/// </summary>
public interface ITrayPolicyRefreshNotifier
{
    Task NotifyTenantAsync(Guid tenantId, CancellationToken ct);
}
