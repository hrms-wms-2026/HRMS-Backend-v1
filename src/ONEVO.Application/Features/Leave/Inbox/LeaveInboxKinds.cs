using System.Text.Json;
using ONEVO.Application.Features.Inbox.Contracts;
using ONEVO.Application.Features.Leave.Approval.RepositoryInterfaces;
using ONEVO.Application.Features.Leave.Approval.Services;

namespace ONEVO.Application.Features.Leave.Inbox;

/// <summary>How leave requests are addressed in the Inbox: ("leave", "request", requestId).</summary>
public static class LeaveInboxKinds
{
    public const string Source = "leave";
    public const string Request = "request";
    public const string Renderer = "leave-request";

    public static readonly InboxKindDescriptor Descriptor = new(
        Source, Request, "Leave request", InboxCategories.Request,
        InboxCapabilities.Chat | InboxCapabilities.EditBeforeApprove | InboxCapabilities.Revert
        | InboxCapabilities.Cancel | InboxCapabilities.Escalate,
        Renderer);
}

public static class LeaveInboxActions
{
    public const string Approve = "approve";
    public const string Reject = "reject";
    public const string ChangeDecision = "change_decision";
    public const string Cancel = "cancel";
    /// <summary>The current approver hands their step to someone else (journal action "escalate").</summary>
    public const string Escalate = "escalate";
}

public sealed record LeaveApproverRowSnapshot(Guid Id, Guid ApproverEmployeeId, string Status, string? Comment, DateTimeOffset? DecidedAt);

/// <summary>What a leave decision changed, kept in the inbox journal (before/after) so it can be undone exactly.</summary>
public sealed record LeaveDecisionSnapshot(
    string Title, string Status, Guid? ApprovedBy, DateTimeOffset? ApprovedAt, decimal? EntitlementPendingHours,
    decimal? EntitlementUsedHours, IReadOnlyList<LeaveApproverRowSnapshot> Approvers, LeaveRangeSnapshot Range)
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public static LeaveDecisionSnapshot Of(LeaveApprovalState state) => new(
        $"{state.LeaveTypeName} - {state.Employee.FirstName} {state.Employee.LastName}".Trim(),
        state.Request.Status, state.Request.ApprovedBy, state.Request.ApprovedAt,
        state.Entitlement?.PendingHours, state.Entitlement?.UsedHours,
        state.Approvers.Select(a => new LeaveApproverRowSnapshot(a.Id, a.ApproverEmployeeId, a.Status, a.Comment, a.DecidedAt)).ToList(),
        LeaveRequestRangeEditor.Snapshot(state.Request));

    public string ToJson() => JsonSerializer.Serialize(this, Json);

    public static LeaveDecisionSnapshot? Parse(string? json)
        => string.IsNullOrWhiteSpace(json) ? null : JsonSerializer.Deserialize<LeaveDecisionSnapshot>(json, Json);
}
