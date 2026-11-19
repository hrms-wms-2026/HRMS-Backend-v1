using ONEVO.Application.Features.WorkManagement.Hierarchy;
using ONEVO.Application.Features.WorkManagement.Monitoring.Services;
using ONEVO.Application.Features.WorkManagement.Projects.RepositoryInterfaces;
using ONEVO.Domain.Features.WorkManagement.Monitoring.Entities;

namespace ONEVO.Application.Features.WorkManagement.Inbox;

/// <summary>The people an alert is for in the Inbox: the employee the monitor notifies (creator-position
/// holder, else project lead - MonitorAlertRecipients, resolved now) plus the employee it is about.</summary>
public sealed class MonitorAlertAudience
{
    private readonly IProjectMonitorSnapshotLoader _snapshots;
    private readonly IWorkHierarchyService _hierarchy;
    private readonly IProjectRepository _projects;

    public MonitorAlertAudience(IProjectMonitorSnapshotLoader snapshots, IWorkHierarchyService hierarchy, IProjectRepository projects)
    {
        _snapshots = snapshots;
        _hierarchy = hierarchy;
        _projects = projects;
    }

    /// <summary>Alert id → recipients, for alerts of one project.</summary>
    public async Task<IReadOnlyDictionary<Guid, IReadOnlyList<Guid>>> ResolveAsync(
        Guid tenantId, Guid projectId, IReadOnlyList<MonitorAlert> alerts, CancellationToken ct)
    {
        var result = new Dictionary<Guid, IReadOnlyList<Guid>>();
        if (alerts.Count == 0)
            return result;

        var snapshot = await _snapshots.LoadAsync(tenantId, projectId, ct);
        var tree = await _hierarchy.LoadTreeAsync(tenantId, projectId, ct);
        var leadId = (await _projects.GetByIdForTenantAsync(tenantId, projectId, ct))?.LeadId;
        foreach (var alert in alerts)
        {
            var holder = await MonitorAlertRecipients.ResolveHolderAsync(
                _hierarchy, tenantId, snapshot, tree, leadId, alert.TargetType, alert.TargetId, ct);
            result[alert.Id] = new[] { holder, alert.SubjectEmployeeId }
                .Where(id => id is { } v && v != Guid.Empty).Select(id => id!.Value).Distinct().ToList();
        }
        return result;
    }
}
