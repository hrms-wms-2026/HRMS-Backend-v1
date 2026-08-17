using System.Text.Json;
using ONEVO.Domain.Features.CoreHr.Entities;

namespace ONEVO.Application.Features.CoreHr.Onboarding.Models;

public static class ChecklistTaskOwnerTypes
{
    public const string Employee = "employee";
    public const string Manager = "manager";
    public const string Hr = "hr";
    public const string It = "it";
    public const string CustomUser = "custom_user";

    /// <summary>Only "employee" is auto-resolved (to the new hire's own user id) at
    /// instantiation time. manager/hr/it/custom_user are informational labels only in Phase 1;
    /// none of them trigger a manager-chain walk or role-based routing lookup.</summary>
    public static readonly IReadOnlySet<string> All = new HashSet<string> { Employee, Manager, Hr, It, CustomUser };
}

public enum ChecklistTaskDueRuleMode
{
    /// <summary>ChecklistTemplate.TasksJson: a template is reused across many future hires
    /// with different start dates, so it stores a relative offset, never a literal date.</summary>
    OffsetDays,

    /// <summary>OnboardingDraft.EditedTasksJson: HR has already reviewed/edited these against
    /// a real anticipated start date in the wizard, so a concrete date is correct here.</summary>
    AbsoluteDate,
}

public sealed record ChecklistTaskDefinition(
    string Title,
    string OwnerType,
    Guid? AssignedToId,
    int? DueOffsetDays,
    DateOnly? DueDate,
    int? Sequence,
    bool IsRequired);

/// <summary>The single strict parser/serializer for checklist task definitions, shared by
/// template CRUD, draft edit validation, and instantiation so there is exactly one definition
/// of what a task looks like.</summary>
public static class ChecklistTaskJsonContract
{
    public static IReadOnlyList<ChecklistTaskDefinition> Parse(string tasksJson, ChecklistTaskDueRuleMode mode)
    {
        try
        {
            using var document = JsonDocument.Parse(tasksJson);
            if (document.RootElement.ValueKind != JsonValueKind.Array)
                throw new ArgumentException("Checklist task JSON must be an array.", nameof(tasksJson));

            var definitions = new List<ChecklistTaskDefinition>();
            foreach (var item in document.RootElement.EnumerateArray())
            {
                definitions.Add(ParseOne(item, mode));
            }
            return definitions;
        }
        catch (JsonException ex)
        {
            throw new ArgumentException("Checklist task JSON is invalid.", nameof(tasksJson), ex);
        }
    }

    private static ChecklistTaskDefinition ParseOne(JsonElement item, ChecklistTaskDueRuleMode mode)
    {
        if (item.ValueKind != JsonValueKind.Object
            || !item.TryGetProperty("title", out var titleEl) || string.IsNullOrWhiteSpace(titleEl.GetString())
            || !item.TryGetProperty("ownerType", out var ownerTypeEl) || ownerTypeEl.GetString() is not { } ownerType
            || !ChecklistTaskOwnerTypes.All.Contains(ownerType)
            || !item.TryGetProperty("isRequired", out var isRequiredEl) || isRequiredEl.ValueKind is not (JsonValueKind.True or JsonValueKind.False))
            throw new ArgumentException("Each checklist task requires a title, a known ownerType, and an explicit isRequired flag.");

        var title = titleEl.GetString()!;
        var isRequired = isRequiredEl.GetBoolean();

        Guid? assignedToId = null;
        var hasAssignedTo = item.TryGetProperty("assignedToId", out var assignedToEl) && assignedToEl.ValueKind != JsonValueKind.Null;
        if (hasAssignedTo)
        {
            if (!Guid.TryParse(assignedToEl.GetString(), out var parsedAssignedTo))
                throw new ArgumentException("assignedToId must be a valid GUID when present.");
            assignedToId = parsedAssignedTo;
        }

        if (ownerType == ChecklistTaskOwnerTypes.Employee)
        {
            if (hasAssignedTo)
                throw new ArgumentException("assignedToId must not be set when ownerType is 'employee' - it is resolved to the new hire at instantiation time.");
        }
        else if (assignedToId is null)
        {
            throw new ArgumentException($"assignedToId is required when ownerType is '{ownerType}'.");
        }

        int? sequence = null;
        if (item.TryGetProperty("sequence", out var sequenceEl) && sequenceEl.ValueKind != JsonValueKind.Null)
        {
            if (!sequenceEl.TryGetInt32(out var parsedSequence))
                throw new ArgumentException("Checklist task sequence must be an integer.");
            sequence = parsedSequence;
        }

        int? dueOffsetDays = null;
        DateOnly? dueDate = null;
        if (mode == ChecklistTaskDueRuleMode.OffsetDays)
        {
            if (!item.TryGetProperty("dueOffsetDays", out var offsetEl) || !offsetEl.TryGetInt32(out var offsetValue) || offsetValue < 0)
                throw new ArgumentException("dueOffsetDays is required and must be a non-negative integer for template task JSON.");
            dueOffsetDays = offsetValue;
        }
        else
        {
            if (!item.TryGetProperty("dueDate", out var dueDateEl) || !DateOnly.TryParse(dueDateEl.GetString(), out var parsedDueDate))
                throw new ArgumentException("dueDate is required and must be a valid date for edited/instantiation task JSON.");
            dueDate = parsedDueDate;
        }

        return new ChecklistTaskDefinition(title, ownerType, assignedToId, dueOffsetDays, dueDate, sequence, isRequired);
    }

    public static string SerializeTemplateTasks(IReadOnlyList<ChecklistTaskDefinition> tasks)
    {
        var payload = tasks.Select(t => new
        {
            title = t.Title,
            ownerType = t.OwnerType,
            assignedToId = t.AssignedToId?.ToString(),
            dueOffsetDays = t.DueOffsetDays,
            sequence = t.Sequence,
            isRequired = t.IsRequired,
        });
        return JsonSerializer.Serialize(payload);
    }

    /// <summary>Resolves a parsed definition set into concrete, insertable
    /// EmployeeChecklistTask rows. Never mutates the source template - callers pass a copy of
    /// the definitions, and this method only constructs new entity instances.</summary>
    public static IReadOnlyList<EmployeeChecklistTask> ToEmployeeChecklistTasks(
        IReadOnlyList<ChecklistTaskDefinition> definitions,
        Guid tenantId,
        Guid employeeId,
        Guid? templateId,
        string lifecycleType,
        Guid newHireUserId,
        DateOnly anchorDate,
        ChecklistTaskDueRuleMode mode)
    {
        return definitions.Select(definition => new EmployeeChecklistTask
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            EmployeeId = employeeId,
            TemplateId = templateId,
            LifecycleType = lifecycleType,
            TaskTitle = definition.Title,
            OwnerType = definition.OwnerType,
            AssignedToId = definition.AssignedToId ?? newHireUserId,
            DueDate = mode == ChecklistTaskDueRuleMode.OffsetDays
                ? anchorDate.AddDays(definition.DueOffsetDays!.Value)
                : definition.DueDate!.Value,
            Sequence = definition.Sequence,
            IsRequired = definition.IsRequired,
            Status = "pending",
        }).ToList();
    }
}
