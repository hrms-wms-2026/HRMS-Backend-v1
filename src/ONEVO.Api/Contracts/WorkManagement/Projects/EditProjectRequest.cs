namespace ONEVO.Api.Contracts.WorkManagement.Projects;

public class EditProjectRequest
{
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public Guid CategoryId { get; set; }
    public DateOnly StartDate { get; set; }
    public DateOnly TargetDate { get; set; }
    public string? Color { get; set; }
    public decimal? ActualHours { get; set; }

    /// <summary>Optional. When set, updates both the Project and its Default Objective allocated hours (root-case extend-allocation, spec §4.3).</summary>
    public decimal? AllocatedHours { get; set; }

    /// <summary>Optional. If present and different from the project's current identifier, the request is rejected with 400 — identifier is immutable after creation.</summary>
    public string? Identifier { get; set; }
}
