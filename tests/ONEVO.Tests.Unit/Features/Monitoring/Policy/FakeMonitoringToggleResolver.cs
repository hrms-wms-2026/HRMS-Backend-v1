using ONEVO.Application.Features.Monitoring.ActivityMonitoring.ServiceInterfaces;

namespace ONEVO.Tests.Unit.Features.Monitoring.Policy;

/// <summary>
/// In-memory stand-in for <see cref="IMonitoringToggleResolver"/> used by policy
/// handler tests. Unset capabilities resolve to the safe default (false), matching
/// the real resolver's behavior when no tenant toggle row exists.
/// </summary>
public sealed class FakeMonitoringToggleResolver : IMonitoringToggleResolver
{
    private readonly Dictionary<MonitoringCapability, bool> _values = new();

    /// <summary>
    /// Set by the test's tenant-switcher callback when SwitchToTenantAsync fires.
    /// Only meaningful in combination with <see cref="WasSwitchedBeforeFirstResolve"/>:
    /// on its own it can't distinguish "switched before this call" from
    /// "switched after this call but before the assertion runs".
    /// </summary>
    public bool IsSwitched { get; set; }

    /// <summary>
    /// Captures whether <see cref="IsSwitched"/> was already true the first time
    /// IsEnabledAsync was invoked, proving the handler switched tenant context
    /// before resolving any toggle rather than just eventually.
    /// </summary>
    public bool? WasSwitchedBeforeFirstResolve { get; private set; }

    public void Set(MonitoringCapability capability, bool enabled) => _values[capability] = enabled;

    public Task<bool> IsEnabledAsync(
        Guid tenantId,
        Guid employeeId,
        MonitoringCapability capability,
        CancellationToken ct = default)
    {
        WasSwitchedBeforeFirstResolve ??= IsSwitched;
        return Task.FromResult(_values.TryGetValue(capability, out var value) && value);
    }
}
