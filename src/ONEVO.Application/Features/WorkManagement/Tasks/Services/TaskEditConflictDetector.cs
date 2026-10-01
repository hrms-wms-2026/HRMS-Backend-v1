using System.Globalization;
using System.Text.Json;
using ONEVO.Application.Features.WorkManagement.Tasks.DTOs;
using ONEVO.Domain.Features.WorkManagement.Tasks.Entities;

namespace ONEVO.Application.Features.WorkManagement.Tasks.Services;

/// <summary>
/// Decides whether an approved task edit is stale. It is stale only when someone else, after the
/// request was made, changed a field that this request also changes. Status moves, clock-in and push
/// bump the task's UpdatedAt but write no edit logs, so on their own they never make a request stale.
/// </summary>
public static class TaskEditConflictDetector
{
    /// <param name="current">The task as it is now.</param>
    /// <param name="request">The pending edit's payload.</param>
    /// <param name="logsAfterSnapshot">Edit logs written after the request's snapshot.</param>
    /// <param name="progressChangedAfterSnapshot">Whether a progress-percent log exists after the snapshot.</param>
    public static bool IsStale(WorkTask current, TaskEditInput request,
        IReadOnlyList<TaskEditLog> logsAfterSnapshot, bool progressChangedAfterSnapshot)
    {
        // Field values as the requester saw them: the current value, rolled back through every later
        // edit (the earliest log's old value wins).
        var snapshot = new Dictionary<string, string?>
        {
            ["title"] = Normalize(current.Title),
            ["description"] = Normalize(current.Description),
            ["priority"] = Normalize(current.Priority),
            ["dueDate"] = Normalize(current.DueDate),
            ["estimatedHours"] = Normalize(current.EstimatedHours),
            ["storyPoints"] = Normalize(current.StoryPoints),
            ["sprintId"] = Normalize(current.SprintId),
        };
        var changedByOthers = new HashSet<string>();
        foreach (var log in logsAfterSnapshot.OrderByDescending(l => l.ChangedAt))
            foreach (var (field, oldValue) in ParseValues(log.OldValuesJson))
            {
                snapshot[field] = oldValue;
                changedByOthers.Add(field);
            }
        if (progressChangedAfterSnapshot)
            changedByOthers.Add("progressPercent");
        if (changedByOthers.Count == 0)
            return false;

        var requested = new Dictionary<string, string?>
        {
            ["title"] = Normalize(request.Title.Trim()),
            ["description"] = Normalize(request.Description?.Trim()),
            ["priority"] = Normalize(request.Priority),
            ["dueDate"] = Normalize(request.DueDate),
            ["estimatedHours"] = Normalize(request.EstimatedHours),
            ["storyPoints"] = Normalize(request.StoryPoints),
        };
        if (request.SprintId is not null)
            requested["sprintId"] = Normalize(request.SprintId);

        var changedByRequest = requested
            .Where(field => snapshot.GetValueOrDefault(field.Key) != field.Value)
            .Select(field => field.Key)
            .ToHashSet();
        if (request.ProgressPercent is not null)
            changedByRequest.Add("progressPercent");

        return changedByRequest.Overlaps(changedByOthers);
    }

    private static IEnumerable<(string Field, string? Value)> ParseValues(string json)
    {
        var values = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(json);
        if (values is null)
            yield break;
        foreach (var (field, element) in values)
            yield return (field, element.ValueKind == JsonValueKind.Null ? null : Normalize(element.ToString()));
    }

    /// <summary>One comparable text form for a field value, whether it came from the entity or from a
    /// JSON log: numbers lose trailing zeros, dates are ISO, Guids lower-case, empty text is null.</summary>
    private static string? Normalize(object? value)
    {
        var text = value switch
        {
            null => null,
            DateOnly date => date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            IFormattable formattable => formattable.ToString(null, CultureInfo.InvariantCulture),
            _ => value.ToString(),
        };
        if (string.IsNullOrEmpty(text))
            return null;
        if (decimal.TryParse(text, NumberStyles.Number, CultureInfo.InvariantCulture, out var number))
            return number.ToString("0.############", CultureInfo.InvariantCulture);
        if (Guid.TryParse(text, out var id))
            return id.ToString();
        return text;
    }
}
