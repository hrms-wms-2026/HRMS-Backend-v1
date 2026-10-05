using System.Text.Json;
using ONEVO.Domain.Features.WorkManagement.Approvals.Entities;

namespace ONEVO.Application.Features.WorkManagement.Approvals.Services;

/// <summary>The one-line summary shown under a request's title on the Approvals page.</summary>
internal static class ApprovalSummaries
{
    public static string? For(WorkApprovalRequest request, JsonElement? payload)
    {
        switch (request.ActionType)
        {
            case WorkActionTypes.ModuleAllocationExtend:
                var hours = ApprovalPayloadJson.GetDecimal(payload, "requestedAdditionalHours");
                var reason = ApprovalPayloadJson.GetString(payload, "reason");
                if (hours is null) return string.IsNullOrWhiteSpace(reason) ? null : reason;
                var h = $"+{ApprovalPayloadJson.FormatNumber(hours.Value)}h";
                return string.IsNullOrWhiteSpace(reason) ? h : $"{h} - {reason}";

            case WorkActionTypes.ProjectStatusTemplateChange:
                return payload is { } root ? DescribeStatusChanges(root) : null;

            case WorkActionTypes.TaskEdit:
            case WorkActionTypes.ModuleEdit:
                var title = ApprovalPayloadJson.GetString(payload, "title")?.Trim();
                return string.IsNullOrWhiteSpace(title) || title == request.TargetTitle ? null : $"Proposed title: {title}";

            default:
                return null;
        }
    }

    /// <summary>"2 added, 1 edited" for a task-status template change set (stored PascalCase - see TaskStatusChangeSet).</summary>
    public static string DescribeStatusChanges(JsonElement root)
    {
        int Count(string name) => ApprovalPayloadJson.TryGet(root, name, out var list, out _) && list.ValueKind == JsonValueKind.Array
            ? list.GetArrayLength()
            : 0;

        var parts = new List<string>();
        var added = Count("Adds");
        var edited = Count("Updates");
        var deleted = Count("Deletes");
        if (added > 0) parts.Add($"{added} added");
        if (edited > 0) parts.Add($"{edited} edited");
        if (deleted > 0) parts.Add($"{deleted} deleted");
        return parts.Count == 0 ? "Reordered statuses" : string.Join(", ", parts);
    }
}
