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

public class TaskCategory : BaseEntity
{
    public Guid ProjectId { get; set; }
    public string Name { get; set; } = string.Empty;
    public int DisplayOrder { get; set; }
    /// <summary>Set only on system-managed categories (can't be deleted or renamed).</summary>
    public string? SystemKey { get; set; }
}
