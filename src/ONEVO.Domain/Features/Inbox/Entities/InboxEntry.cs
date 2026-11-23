using ONEVO.Domain.Common;

namespace ONEVO.Domain.Features.Inbox.Entities;

/// <summary>Values of <see cref="InboxEntry.Status"/> (database check constraint).</summary>
public static class InboxEntryStatuses
{
    public const string Pending = "pending";
    public const string Approved = "approved";
    public const string Rejected = "rejected";
    public const string Cancelled = "cancelled";
    public const string Resolved = "resolved";

    public static readonly IReadOnlyList<string> All = [Pending, Approved, Rejected, Cancelled, Resolved];
}

/// <summary>Values of <see cref="InboxEntry.Category"/> (database check constraint).</summary>
public static class InboxEntryCategories
{
    public const string Request = "request";
    public const string Alert = "alert";
    public const string Invitation = "invitation";

    public static readonly IReadOnlyList<string> All = [Request, Alert, Invitation];
}

/// <summary>Values of <see cref="InboxEntryAssignee.Status"/> (database check constraint).</summary>
public static class InboxEntryAssigneeStatuses
{
    /// <summary>Must act now: this row is what the inbox counts.</summary>
    public const string Pending = "pending";
    /// <summary>In the chain but not their turn yet (an in-order approval waiting for an earlier step).</summary>
    public const string Waiting = "waiting";
    public const string Approved = "approved";
    public const string Rejected = "rejected";
    public const string Skipped = "skipped";
    public const string Cancelled = "cancelled";

    public static readonly IReadOnlyList<string> All = [Pending, Waiting, Approved, Rejected, Skipped, Cancelled];
}

/// <summary>
/// Task 37: one row per inbox item (request, alert, invitation) of a converted module, written by the module in the
/// same transaction as its own change. The module's table stays the source of truth for details; this header answers
/// "who must act" and "what did I send" with indexed queries. Converted so far: leave requests.
/// </summary>
public class InboxEntry : ITenantOwnedEntity
{
    public Guid Id { get; set; }
    public Guid TenantId { get; set; }
    public string Source { get; set; } = string.Empty;
    public string Kind { get; set; } = string.Empty;
    /// <summary>The row in the module's own table.</summary>
    public Guid SubjectId { get; set; }
    public string Category { get; set; } = InboxEntryCategories.Request;
    /// <summary>Who it is about.</summary>
    public Guid? SubjectEmployeeId { get; set; }
    public Guid? RequestedByEmployeeId { get; set; }
    public string Status { get; set; } = InboxEntryStatuses.Pending;
    public string Title { get; set; } = string.Empty;
    public string? Summary { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
    public DateTimeOffset? DecidedAt { get; set; }
    public Guid? DecidedByEmployeeId { get; set; }

    public List<InboxEntryAssignee> Assignees { get; set; } = [];
}

/// <summary>
/// One person in an item's approval chain. Source, Kind and UpdatedAt are copied from the header so the inbox counts
/// ("pending for me, by kind") are one indexed query on this table.
/// </summary>
public class InboxEntryAssignee : ITenantOwnedEntity
{
    public Guid Id { get; set; }
    public Guid TenantId { get; set; }
    public Guid InboxEntryId { get; set; }
    public Guid EmployeeId { get; set; }
    public string Source { get; set; } = string.Empty;
    public string Kind { get; set; } = string.Empty;
    public string Status { get; set; } = InboxEntryAssigneeStatuses.Pending;
    public int SequenceOrder { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
}
