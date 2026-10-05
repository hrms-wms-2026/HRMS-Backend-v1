using ONEVO.Domain.Common;

namespace ONEVO.Domain.Features.WorkManagement.Projects.Entities;

public static class ProjectSystemPurposes
{
    public const string Office = "office";
}

public class Project : BaseEntity
{
    public string? SystemPurpose { get; set; }
    public Guid OwningLegalEntityId { get; set; }
    public Guid CategoryId { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Identifier { get; set; } = string.Empty;
    public long NextTaskNumber { get; set; } = 1;
    public string? Description { get; set; }
    public Guid LeadId { get; set; }
    public DateOnly StartDate { get; set; }
    public DateOnly TargetDate { get; set; }
    public string? Color { get; set; }
    public decimal? ActualHours { get; set; }
    public decimal AllocatedHours { get; set; }
    public decimal CompletedHours { get; set; }
    public bool IsActive { get; set; } = true;
    public bool IsAchieved { get; set; }
    public DateTimeOffset? AchievedAt { get; set; }
}
