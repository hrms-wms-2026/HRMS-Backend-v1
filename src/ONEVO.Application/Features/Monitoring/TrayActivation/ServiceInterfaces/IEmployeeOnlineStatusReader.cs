namespace ONEVO.Application.Features.Monitoring.TrayActivation.ServiceInterfaces;

/// <summary>
/// Which employees are online right now: within the presence grace period their user made a
/// request from the web app, or has a tray holding a live agent-hub connection, or the tray
/// heartbeated. Answers many employees at once (one query, keyed by employee id).
/// </summary>
public interface IEmployeeOnlineStatusReader
{
    Task<IReadOnlySet<Guid>> GetOnlineEmployeeIdsAsync(
        Guid tenantId,
        IReadOnlyCollection<Guid> employeeIds,
        CancellationToken ct);
}
