using System.Text.Json;
using System.Text.Json.Nodes;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.Auth.Login.RepositoryInterfaces;
using ONEVO.Domain.Features.Auth.Entities;
using ONEVO.Domain.Features.WorkManagement.Meetings.Entities;

namespace ONEVO.Application.Features.WorkManagement.Meetings.Services;

/// <summary>The audit_logs action names for meeting delegates (spec "Audit log").</summary>
public static class MeetingAuditActions
{
    public const string DelegateGranted = "meeting_delegate.granted";
    public const string DelegateRevoked = "meeting_delegate.revoked";
    public const string DelegateUpdated = "meeting_delegate.updated";
    public const string Created = "meeting.created_for_principal";
    public const string Edited = "meeting.edited_for_principal";
    public const string Moved = "meeting.moved_for_principal";
    public const string Cancelled = "meeting.cancelled_for_principal";
    public const string AttendeesChanged = "meeting.attendees_changed_for_principal";
    public const string LinkChanged = "meeting.link_changed_for_principal";
    // Delegate task planning (2026-11-11): a planning delegate's writes on the principal's planner. A resize is a move.
    public const string BlockPlanned = "planner.block_planned_for_principal";
    public const string BlockMoved = "planner.block_moved_for_principal";
    public const string BlockRemoved = "planner.block_removed_for_principal";
}

/// <summary>
/// Writes the audit rows for meeting delegates. The current user is the actor; the principal goes into the JSON. Rows are
/// added to the unit of work, so they commit with the action they describe. Never calls SaveChangesAsync.
/// </summary>
public interface IMeetingDelegateAudit
{
    /// <summary>A grant or a revoke of <paramref name="row"/>.</summary>
    Task RecordDelegationAsync(MeetingDelegate row, string action, CancellationToken ct);

    /// <summary>A planning delegate's write on the principal's planner (resource PlannedTaskBlock).</summary>
    Task RecordBlockForPrincipalAsync(Guid tenantId, Guid principalEmployeeId, Guid blockId, string action, object newValues,
        object? oldValues, CancellationToken ct);

    /// <summary>"Can also plan my tasks" turned on or off: old and new value.</summary>
    Task RecordDelegationChangeAsync(MeetingDelegate row, bool oldCanPlanTasks, CancellationToken ct);

    /// <summary>
    /// An action a delegate took on <paramref name="series"/> for <paramref name="actingForEmployeeId"/>. A private
    /// meeting's title is masked, and guest emails, descriptions and notes are dropped, whatever the caller passes.
    /// </summary>
    Task RecordForPrincipalAsync(MeetingSeries series, Guid actingForEmployeeId, string action, object newValues, object? oldValues,
        CancellationToken ct);

    /// <summary>The same, by id, for a meeting that is not loaded (a create), or does not exist yet (a create that went for
    /// approval: <paramref name="seriesId"/> is null).</summary>
    Task RecordForPrincipalAsync(Guid tenantId, Guid? seriesId, bool isPrivate, Guid actingForEmployeeId, string action, object newValues,
        object? oldValues, CancellationToken ct);

    /// <summary>
    /// For an organizer-only write that has already passed its organizer check: a caller other than the series' organizer
    /// can only be one of the organizer's delegates, so the action is recorded for the organizer. The organizer's own
    /// action writes nothing.
    /// </summary>
    Task RecordIfDelegatedAsync(MeetingSeries series, Guid callerEmployeeId, string action, object newValues, object? oldValues,
        CancellationToken ct);
}

public sealed class MeetingDelegateAudit : IMeetingDelegateAudit
{
    private const string PrivateTitle = "(private)";
    private static readonly string[] NeverLogged = ["guestEmails", "guests", "description", "notes"];

    private readonly IAuditLogRepository _logs;
    private readonly ICurrentUser _currentUser;
    private readonly IDateTimeProvider _clock;

    public MeetingDelegateAudit(IAuditLogRepository logs, ICurrentUser currentUser, IDateTimeProvider clock)
    {
        _logs = logs;
        _currentUser = currentUser;
        _clock = clock;
    }

    public Task RecordDelegationAsync(MeetingDelegate row, string action, CancellationToken ct)
    {
        var revoked = action == MeetingAuditActions.DelegateRevoked;
        var newValues = new JsonObject
        {
            ["principalEmployeeId"] = row.PrincipalEmployeeId,
            ["delegateEmployeeId"] = row.DelegateEmployeeId,
        };
        if (!revoked)
        {
            newValues["expiresAt"] = row.ExpiresAt;
            newValues["canPlanTasks"] = row.CanPlanTasks;
        }
        var oldValues = revoked ? new JsonObject { ["expiresAt"] = row.ExpiresAt } : null;
        return AddAsync(row.TenantId, action, "MeetingDelegate", row.Id, newValues, oldValues, ct);
    }

    public Task RecordBlockForPrincipalAsync(Guid tenantId, Guid principalEmployeeId, Guid blockId, string action, object newValues,
        object? oldValues, CancellationToken ct)
    {
        var next = new JsonObject { ["actingForEmployeeId"] = principalEmployeeId };
        foreach (var (key, value) in Clean(newValues, isPrivate: false) ?? [])
            next[key] = value?.DeepClone();
        return AddAsync(tenantId, action, "PlannedTaskBlock", blockId, next, Clean(oldValues, isPrivate: false), ct);
    }

    public Task RecordDelegationChangeAsync(MeetingDelegate row, bool oldCanPlanTasks, CancellationToken ct)
        => AddAsync(row.TenantId, MeetingAuditActions.DelegateUpdated, "MeetingDelegate", row.Id,
            new JsonObject
            {
                ["principalEmployeeId"] = row.PrincipalEmployeeId,
                ["delegateEmployeeId"] = row.DelegateEmployeeId,
                ["canPlanTasks"] = row.CanPlanTasks,
            },
            new JsonObject { ["canPlanTasks"] = oldCanPlanTasks }, ct);

    public Task RecordForPrincipalAsync(MeetingSeries series, Guid actingForEmployeeId, string action, object newValues, object? oldValues,
        CancellationToken ct)
        => RecordForPrincipalAsync(series.TenantId, series.Id, series.IsPrivate, actingForEmployeeId, action, newValues, oldValues, ct);

    public Task RecordForPrincipalAsync(Guid tenantId, Guid? seriesId, bool isPrivate, Guid actingForEmployeeId, string action, object newValues,
        object? oldValues, CancellationToken ct)
    {
        var next = new JsonObject { ["actingForEmployeeId"] = actingForEmployeeId };
        foreach (var (key, value) in Clean(newValues, isPrivate) ?? [])
            next[key] = value?.DeepClone();
        return AddAsync(tenantId, action, "MeetingSeries", seriesId, next, Clean(oldValues, isPrivate), ct);
    }

    public Task RecordIfDelegatedAsync(MeetingSeries series, Guid callerEmployeeId, string action, object newValues, object? oldValues,
        CancellationToken ct)
        => callerEmployeeId == series.OrganizerEmployeeId
            ? Task.CompletedTask
            : RecordForPrincipalAsync(series, series.OrganizerEmployeeId, action, newValues, oldValues, ct);

    private static JsonObject? Clean(object? values, bool isPrivate)
    {
        if (values is null)
            return null;
        var json = JsonSerializer.SerializeToNode(values)?.AsObject() ?? [];
        foreach (var key in NeverLogged)
            json.Remove(key);
        if (isPrivate && json.ContainsKey("title"))
            json["title"] = PrivateTitle;
        return json;
    }

    private Task AddAsync(Guid tenantId, string action, string resourceType, Guid? resourceId, JsonObject newValues, JsonObject? oldValues,
        CancellationToken ct)
        => _logs.AddAsync(new AuditLog
        {
            Id = Guid.NewGuid(), TenantId = tenantId, UserId = _currentUser.UserId, Action = action,
            ResourceType = resourceType, ResourceId = resourceId,
            NewValuesJson = newValues.ToJsonString(), OldValuesJson = oldValues?.ToJsonString(),
            CreatedAt = _clock.UtcNow,
        }, ct);
}
