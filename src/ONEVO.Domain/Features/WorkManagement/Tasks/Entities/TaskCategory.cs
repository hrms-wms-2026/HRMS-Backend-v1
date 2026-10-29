using ONEVO.Domain.Common;

namespace ONEVO.Domain.Features.WorkManagement.Tasks.Entities;

public static class TaskCategorySystemKeys
{
    public const string Meeting = "meeting";

    /// <summary>True for any casing of a system category's name, after trimming. Users can't
    /// create or rename a category to one of these.</summary>
    public static bool IsReservedName(string name) =>
        string.Equals(name.Trim(), Meeting, StringComparison.OrdinalIgnoreCase);
}

/// <summary>
/// A category's fixed type (Planner redesign, 2026-10-29): names are free text per project, so the UI colours, icons and
/// groups by this instead. Meeting and Checklist belong to system categories; users pick from the other five.
/// </summary>
public static class TaskCategoryKinds
{
    public const string Task = "task";
    public const string Bug = "bug";
    public const string Story = "story";
    public const string Feature = "feature";
    public const string Testing = "testing";
    public const string Meeting = "meeting";
    public const string Checklist = "checklist";

    /// <summary>Display order.</summary>
    public static readonly IReadOnlyList<string> All = [Task, Bug, Story, Feature, Testing, Meeting, Checklist];

    /// <summary>The kinds a user may give a category; Meeting and Checklist are set by the system only.</summary>
    public static readonly IReadOnlyList<string> UserSelectable = [Task, Bug, Story, Feature, Testing];

    /// <summary>The kind a name suggests (trimmed, any casing); anything not recognised is a Task.</summary>
    public static string Infer(string name, string? systemKey)
    {
        if (systemKey == TaskCategorySystemKeys.Meeting)
            return Meeting;
        return name.Trim().ToLowerInvariant() switch
        {
            "bug" or "bugs" or "defect" or "defects" => Bug,
            "story" or "stories" or "user story" => Story,
            "feature" or "features" or "enhancement" => Feature,
            "test" or "tests" or "testing" or "qa" => Testing,
            "onboarding" or "offboarding" => Checklist,
            _ => Task,
        };
    }
}

public static class TaskCategoryErrorCodes
{
    /// <summary>400: an unknown kind, a system kind (meeting, checklist), or a change to a system category's kind.</summary>
    public const string InvalidKind = "invalid_category_kind";
}

public class TaskCategory : BaseEntity
{
    public Guid ProjectId { get; set; }
    public string Name { get; set; } = string.Empty;
    public int DisplayOrder { get; set; }
    /// <summary>Set only on system-managed categories (can't be deleted or renamed).</summary>
    public string? SystemKey { get; set; }
    /// <summary>One of <see cref="TaskCategoryKinds"/>. A rename never changes it.</summary>
    public string Kind { get; set; } = TaskCategoryKinds.Task;
}
