namespace ONEVO.Application.Features.Monitoring.TrayActivation.ServiceInterfaces;

/// <summary>
/// Decides whether the current user must have the ONEVO tray app connected. This is data-driven,
/// not a global switch: the tenant must have the monitoring module, and at least one monitoring
/// capability must resolve to enabled for this user (employee override → work mode → role/position/
/// department → tenant toggle). Admins, unmonitored employees and tenants without monitoring are
/// never gated.
/// </summary>
public interface ITrayPresenceRequirementEvaluator
{
    Task<bool> IsRequiredForCurrentUserAsync(CancellationToken ct);
}
