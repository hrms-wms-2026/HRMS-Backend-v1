using Microsoft.AspNetCore.Http;

namespace ONEVO.Api.Contracts.WorkManagement.Projects;

public class CreateProjectFormRequest
{
    public Guid CategoryId { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Identifier { get; set; } = string.Empty;
    public string? Description { get; set; }
    public DateOnly StartDate { get; set; }
    public DateOnly TargetDate { get; set; }
    public DateOnly? ReleaseDate { get; set; }
    public string? Color { get; set; }
    public decimal? ActualHours { get; set; }
    public decimal DefaultObjectiveAllocatedHours { get; set; }

    /// <summary>JSON-encoded array of {"name":"...","color":"..."} objects, sent as a multipart string field.</summary>
    public string? LabelsJson { get; set; }

    public IFormFile? Logo { get; set; }
    public IFormFile? Banner { get; set; }
}
