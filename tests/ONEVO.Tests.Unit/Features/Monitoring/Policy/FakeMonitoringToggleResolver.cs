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

    public void Set(MonitoringCapability capability, bool enabled) => _values[capability] = enabled;

    public Task<bool> IsEnabledAsync(
        Guid tenantId,
        Guid employeeId,
        MonitoringCapability capability,
        CancellationToken ct = default)
    {
        return Task.FromResult(_values.TryGetValue(capability, out var value) && value);
    }
}
