using System.Text.Json;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.WorkManagement.Hierarchy;
using ONEVO.Application.Features.WorkManagement.Monitoring.RepositoryInterfaces;
using ONEVO.Application.Features.WorkManagement.Notifications.Services;
using ONEVO.Application.Features.WorkManagement.Projects.RepositoryInterfaces;
using ONEVO.Domain.Features.WorkManagement.Monitoring.Entities;
using ONEVO.Domain.Features.WorkManagement.Notifications.Entities;

namespace ONEVO.Application.Features.WorkManagement.Monitoring.Services;

public interface IProjectMonitorService
{
    /// <summary>Runs every monitor rule on one project, opens alerts for new problems (notifying the
    /// target's creator position once), refreshes the ones still present and resolves the ones that
    /// are gone. Does NOT save - the caller commits. Returns how many new alerts were opened.</summary>
    Task<int> EvaluateProjectAsync(Guid tenantId, Guid projectId, DateOnly today, CancellationToken ct = default);
}

public sealed class ProjectMonitorService : IProjectMonitorService
{
    private readonly IProjectMonitorSnapshotLoader _snapshots;
    private readonly IMonitorAlertRepository _alerts;
    private readonly IWorkHierarchyService _hierarchy;
    private readonly IProjectRepository _projects;
    private readonly IWorkNotificationEngine _notifications;
    private readonly IDateTimeProvider _clock;

    public ProjectMonitorService(
        IProjectMonitorSnapshotLoader snapshots, IMonitorAlertRepository alerts, IWorkHierarchyService hierarchy,
        IProjectRepository projects, IWorkNotificationEngine notifications, IDateTimeProvider clock)
    {
        _snapshots = snapshots;
        _alerts = alerts;
        _hierarchy = hierarchy;
        _projects = projects;
        _notifications = notifications;
        _clock = clock;
    }

    public async Task<int> EvaluateProjectAsync(Guid tenantId, Guid projectId, DateOnly today, CancellationToken ct = default)
    {
        var snapshot = await _snapshots.LoadAsync(tenantId, projectId, ct);
        var findings = ProjectMonitorRules.Evaluate(snapshot, today)
            .GroupBy(Key).Select(g => g.First())
            .ToList();
        var open = (await _alerts.ListOpenTrackedForProjectAsync(tenantId, projectId, ct))
            .GroupBy(a => (a.TargetId, a.RuleCode, a.SubjectEmployeeId))
            .ToDictionary(g => g.Key, g => g.First());

        var now = _clock.UtcNow;
        var seen = new HashSet<(Guid, string, Guid?)>();
        var opened = new List<(MonitorAlert Alert, MonitorFinding Finding)>();
        foreach (var finding in findings)
        {
            var key = Key(finding);
            seen.Add(key);
            if (open.TryGetValue(key, out var existing))
            {
                existing.LastSeenAt = now;
                existing.Message = finding.Message;
                existing.TargetTitle = finding.TargetTitle;
                existing.DetailsJson = JsonSerializer.Serialize(finding.Details);
                continue;
            }

            var alert = new MonitorAlert
            {
                Id = Guid.NewGuid(), TenantId = tenantId, ProjectId = projectId, TargetType = finding.TargetType,
                TargetId = finding.TargetId, TargetTitle = finding.TargetTitle, RuleCode = finding.RuleCode,
                SubjectEmployeeId = finding.SubjectEmployeeId, Message = finding.Message,
                DetailsJson = JsonSerializer.Serialize(finding.Details), FirstDetectedAt = now, LastSeenAt = now, CreatedAt = now
            };
            await _alerts.AddAsync(alert, ct);
            opened.Add((alert, finding));
        }

        foreach (var (key, alert) in open)
            if (!seen.Contains(key))
                alert.ResolvedAt = now;

        // An achieved Module's alerts (and those of everything under it) are kept for display only -
        // nobody is asked to act on finished work, so they are never notified.
        var achievedScope = AchievedModuleScope(snapshot);
        var actionable = opened.Where(o => !InAchievedScope(o.Finding, snapshot, achievedScope)).ToList();
        if (actionable.Count > 0)
            await NotifyAsync(tenantId, projectId, snapshot, actionable, now, ct);

        return opened.Count;
    }

    /// <summary>Ids of every achieved Module and all Modules below one.</summary>
    private static HashSet<Guid> AchievedModuleScope(ProjectMonitorSnapshot snapshot)
    {
        var childrenByParent = snapshot.Modules.Where(m => m.ParentId is not null).ToLookup(m => m.ParentId!.Value);
        var scope = new HashSet<Guid>();
        var queue = new Queue<Guid>(snapshot.Modules.Where(m => m.IsAchieved).Select(m => m.Id));
        while (queue.Count > 0)
        {
            var current = queue.Dequeue();
            if (!scope.Add(current))
                continue;
            foreach (var child in childrenByParent[current])
                queue.Enqueue(child.Id);
        }
        return scope;
    }

    private static bool InAchievedScope(MonitorFinding finding, ProjectMonitorSnapshot snapshot, HashSet<Guid> achievedScope)
        => finding.TargetType switch
        {
            MonitorTargetTypes.Module => achievedScope.Contains(finding.TargetId),
            MonitorTargetTypes.Task => snapshot.Tasks.FirstOrDefault(t => t.Id == finding.TargetId) is { } task
                && achievedScope.Contains(task.ModuleId),
            _ => false,
        };

    private async Task NotifyAsync(Guid tenantId, Guid projectId, ProjectMonitorSnapshot snapshot,
        IReadOnlyList<(MonitorAlert Alert, MonitorFinding Finding)> opened, DateTimeOffset now, CancellationToken ct)
    {
        var tree = await _hierarchy.LoadTreeAsync(tenantId, projectId, ct);
        var project = await _projects.GetByIdForTenantAsync(tenantId, projectId, ct);

        foreach (var (alert, finding) in opened)
        {
            var position = CreatorPosition(finding, snapshot, tree);
            var recipient = position is { } positionId
                ? await _hierarchy.FindActiveHolderAsync(tenantId, tree, positionId, Guid.Empty, ct)
                : null;
            recipient ??= project?.LeadId;
            if (recipient is null)
                continue;

            await _notifications.NotifyAsync(new WorkNotificationEvent(
                tenantId, projectId, Guid.Empty, WorkNotificationKinds.Alert,
                WorkActionLabels.MonitorPrefix + finding.RuleCode, finding.TargetType, finding.TargetId, finding.TargetTitle,
                ApprovalRequestId: null, [recipient.Value]), ct);
            alert.NotifiedAt = now;
        }
    }

    /// <summary>The Module whose owner holds the target's creator position: the stored creator
    /// position, else the same defaults the approval engine uses (task → its Module, sprint → the root
    /// Module, Module → its parent, or itself at the root).</summary>
    private static Guid? CreatorPosition(MonitorFinding finding, ProjectMonitorSnapshot snapshot, ProjectModuleTree tree)
        => finding.TargetType switch
        {
            MonitorTargetTypes.Task => snapshot.Tasks.FirstOrDefault(t => t.Id == finding.TargetId) is { } task
                ? task.CreatorPositionModuleId ?? task.ModuleId
                : null,
            MonitorTargetTypes.Sprint => snapshot.Sprints.FirstOrDefault(s => s.Id == finding.TargetId)?.CreatorPositionModuleId
                ?? tree.Root?.Id,
            _ => snapshot.Modules.FirstOrDefault(m => m.Id == finding.TargetId) is { } module
                ? module.CreatorPositionModuleId ?? module.ParentId ?? module.Id
                : null,
        };

    private static (Guid, string, Guid?) Key(MonitorFinding finding) => (finding.TargetId, finding.RuleCode, finding.SubjectEmployeeId);
}
