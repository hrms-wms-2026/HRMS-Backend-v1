using System.Globalization;
using System.Text.Json;
using ONEVO.Application.Features.WorkManagement.Approvals.DTOs;
using ONEVO.Domain.Features.WorkManagement.Approvals.Entities;

namespace ONEVO.Application.Features.WorkManagement.Approvals.Services;

/// <summary>Builds the "current → requested (→ applied)" rows of an approval's explanation card.
/// Pure: the caller loads the current values (camelCase keys, display-ready strings).</summary>
public static class ApprovalChangeSetBuilder
{
    private sealed record FieldSpec(string Name, string Label, string Kind);

    private static readonly FieldSpec[] TaskFields =
    [
        new("title", "Title", "text"), new("description", "Description", "longtext"), new("priority", "Priority", "priority"),
        new("dueDate", "Due date", "date"), new("estimatedHours", "Estimated hours", "hours"), new("storyPoints", "Story points", "number")
    ];

    private static readonly Dictionary<string, FieldSpec[]> Specs = new()
    {
        [WorkActionTypes.TaskCreate] = TaskFields,
        [WorkActionTypes.TaskEdit] = [.. TaskFields, new("progressPercent", "Progress %", "number")],
        [WorkActionTypes.ModuleEdit] =
        [
            new("title", "Title", "text"), new("description", "Description", "longtext"), new("startDate", "Start date", "date"),
            new("endDate", "End date", "date"), new("allocatedHours", "Allocated hours", "hours")
        ],
        [WorkActionTypes.ModuleAllocationExtend] = [new("requestedAdditionalHours", "Additional hours", "hours")],
        [WorkActionTypes.ModuleTransfer] = [new("newHeadEmployeeId", "New owner", "employee")],
        [WorkActionTypes.SprintCreate] = [new("name", "Name", "text"), new("goal", "Goal", "longtext")],
        [WorkActionTypes.SprintEdit] =
        [
            new("name", "Name", "text"), new("goal", "Goal", "longtext"),
            new("startDate", "Start date", "date"), new("endDate", "End date", "date")
        ],
        [WorkActionTypes.SprintStart] =
        [
            new("startDate", "Start date", "date"), new("endDate", "End date", "date"), new("goal", "Goal", "longtext")
        ],
    };

    /// <summary>D5: the actions whose values an approver may change before approving.</summary>
    public static readonly IReadOnlySet<string> EditableActions = new HashSet<string>
    {
        WorkActionTypes.TaskCreate, WorkActionTypes.TaskEdit, WorkActionTypes.ModuleEdit,
        WorkActionTypes.ModuleAllocationExtend, WorkActionTypes.SprintCreate, WorkActionTypes.SprintEdit
    };

    private static readonly HashSet<string> AllRequestedCountAsChanged =
        [WorkActionTypes.TaskCreate, WorkActionTypes.SprintCreate, WorkActionTypes.ModuleAllocationExtend];

    public static IReadOnlyList<ApprovalFieldResponse> Build(
        string actionType,
        string? requestedJson,
        string? appliedJson,
        IReadOnlyDictionary<string, string?> current,
        bool editable,
        IReadOnlyDictionary<string, string?>? requestedOverrides = null)
    {
        if (!Specs.TryGetValue(actionType, out var specs))
            return [];

        var requested = ApprovalPayloadJson.Parse(requestedJson);
        var applied = ApprovalPayloadJson.Parse(appliedJson);
        var actionEditable = editable && EditableActions.Contains(actionType);

        return specs.Select(spec =>
        {
            var found = ApprovalPayloadJson.TryGet(requested, spec.Name, out var requestedValue, out var key);
            var requestedText = requestedOverrides is not null && requestedOverrides.TryGetValue(spec.Name, out var overridden)
                ? overridden
                : found ? Format(requestedValue, spec.Kind) : null;
            var appliedText = ApprovalPayloadJson.TryGet(applied, spec.Name, out var appliedValue, out _)
                ? Format(appliedValue, spec.Kind)
                : null;
            var currentText = current.GetValueOrDefault(spec.Name);
            var changed = AllRequestedCountAsChanged.Contains(actionType)
                ? requestedText is not null
                : !string.Equals(currentText, requestedText, StringComparison.Ordinal);

            return new ApprovalFieldResponse(
                key, spec.Label, spec.Kind, currentText, requestedText, appliedText, changed,
                actionEditable && spec.Kind != "longtext");
        }).ToList();
    }

    /// <summary>Display-ready value: dates yyyy-MM-dd, numbers invariant "0.##", strings trimmed, JSON null → null.</summary>
    private static string? Format(JsonElement value, string kind)
    {
        switch (value.ValueKind)
        {
            case JsonValueKind.Null:
            case JsonValueKind.Undefined:
                return null;
            case JsonValueKind.Number:
                return value.TryGetDecimal(out var d) ? ApprovalPayloadJson.FormatNumber(d) : value.GetRawText();
            case JsonValueKind.String:
                var s = value.GetString()?.Trim();
                if (string.IsNullOrEmpty(s)) return null;
                if (kind == "date" && DateTime.TryParse(s, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var date))
                    return date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
                return s;
            case JsonValueKind.True:
                return "true";
            case JsonValueKind.False:
                return "false";
            default:
                return value.GetRawText();
        }
    }
}
