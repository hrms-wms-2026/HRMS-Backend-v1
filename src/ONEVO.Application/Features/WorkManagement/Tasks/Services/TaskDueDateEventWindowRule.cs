using ONEVO.Application.Features.WorkManagement.CalendarEvents.RepositoryInterfaces;

namespace ONEVO.Application.Features.WorkManagement.Tasks.Services;

/// <summary>
/// R3: a member of an active calendar event must keep a due date inside every such event's window.
/// Shared by EditTask and SetTaskAttributes so the rule cannot drift between them.
/// </summary>
public static class TaskDueDateEventWindowRule
{
    public static string? Validate(IReadOnlyList<ActiveEventWindow> windows, DateOnly? dueDate)
    {
        if (windows.Count == 0)
            return null;

        if (dueDate is null)
            return $"This task is in active event(s) {string.Join(", ", windows.Select(w => w.Name))}; a due date is required.";

        var bad = windows.Where(w => dueDate < w.StartDate || dueDate > w.EndDate).ToList();
        return bad.Count == 0
            ? null
            : $"Due date {dueDate:yyyy-MM-dd} is outside event window(s): "
              + $"{string.Join(", ", bad.Select(w => $"{w.Name} {w.StartDate:yyyy-MM-dd}..{w.EndDate:yyyy-MM-dd}"))}. Widen the event first.";
    }
}
