using ONEVO.Application.Features.WorkManagement.Hierarchy;

namespace ONEVO.Application.Features.WorkManagement.Monitoring.Services;

/// <summary>Who a monitor alert is for: the active holder of the target's creator position, else the
/// project lead. The single rule behind both the monitor's notification and the Inbox's alert recipients.</summary>
public static class MonitorAlertRecipients
{
    /// <summary>The Module whose owner holds the target's creator position: the stored creator
    /// position, else the same defaults the approval engine uses (task → its Module, sprint → the root
    /// Module, Module → its parent, or itself at the root).</summary>
    public static Guid? CreatorPosition(string targetType, Guid targetId, ProjectMonitorSnapshot snapshot, ProjectModuleTree tree)
        => targetType switch
        {
            MonitorTargetTypes.Task => snapshot.Tasks.FirstOrDefault(t => t.Id == targetId) is { } task
                ? task.CreatorPositionModuleId ?? task.ModuleId
                : null,
            MonitorTargetTypes.Sprint => snapshot.Sprints.FirstOrDefault(s => s.Id == targetId)?.CreatorPositionModuleId
                ?? tree.Root?.Id,
            _ => snapshot.Modules.FirstOrDefault(m => m.Id == targetId) is { } module
                ? module.CreatorPositionModuleId ?? module.ParentId ?? module.Id
                : null,
        };

    /// <summary>The employee notified about an alert on (targetType, targetId); null if nobody qualifies.</summary>
    public static async Task<Guid?> ResolveHolderAsync(
        IWorkHierarchyService hierarchy, Guid tenantId, ProjectMonitorSnapshot snapshot, ProjectModuleTree tree,
        Guid? projectLeadId, string targetType, Guid targetId, CancellationToken ct)
    {
        var position = CreatorPosition(targetType, targetId, snapshot, tree);
        var holder = position is { } positionId
            ? await hierarchy.FindActiveHolderAsync(tenantId, tree, positionId, Guid.Empty, ct)
            : null;
        return holder ?? projectLeadId;
    }

    /// <summary>ResolveHolderAsync for many targets of one project, with one employee read for all of them.</summary>
    public static async Task<IReadOnlyDictionary<(string TargetType, Guid TargetId), Guid?>> ResolveHoldersAsync(
        IWorkHierarchyService hierarchy, Guid tenantId, ProjectMonitorSnapshot snapshot, ProjectModuleTree tree,
        Guid? projectLeadId, IEnumerable<(string TargetType, Guid TargetId)> targets, CancellationToken ct)
    {
        var positions = targets.Distinct().ToDictionary(t => t, t => CreatorPosition(t.TargetType, t.TargetId, snapshot, tree));
        var holders = await hierarchy.FindActiveHoldersAsync(
            tenantId, tree, positions.Values.OfType<Guid>().Distinct().ToList(), ct);
        return positions.ToDictionary(p => p.Key, p => (p.Value is { } positionId ? holders.GetValueOrDefault(positionId) : null) ?? projectLeadId);
    }
}
