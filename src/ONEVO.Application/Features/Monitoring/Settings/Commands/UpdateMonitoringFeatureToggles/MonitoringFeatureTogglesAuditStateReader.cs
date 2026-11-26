using ONEVO.Application.Common.Auditing;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.Monitoring.Settings.Helpers;
using ONEVO.Application.Features.Monitoring.Settings.RepositoryInterfaces;

namespace ONEVO.Application.Features.Monitoring.Settings.Commands.UpdateMonitoringFeatureToggles;

/// <summary>
/// Gives the monitoring.toggles_updated audit row its before and after values (the company's
/// toggle fields), so "who switched activity monitoring off, and when" is answerable from
/// audit_logs. Reads the same row the handler edits: the active company's own settings, without
/// the tenant-wide fallback. Null when the company has no settings row yet.
/// </summary>
public sealed class MonitoringFeatureTogglesAuditStateReader(
    IMonitoringFeatureTogglesRepository toggles,
    ICurrentUser currentUser)
    : IAuditStateReader<UpdateMonitoringFeatureTogglesCommand>
{
    public async Task<object?> ReadAsync(UpdateMonitoringFeatureTogglesCommand command, CancellationToken ct)
    {
        if (currentUser.TenantId == Guid.Empty || currentUser.LegalEntityId is not Guid legalEntityId)
            return null;

        var row = await toggles.GetByLegalEntityIdAsync(
            currentUser.TenantId, legalEntityId, includeTenantFallback: false, ct);
        return row is null ? null : MonitoringSettingsAudit.Projection(row);
    }
}
