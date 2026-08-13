namespace ONEVO.Api.Contracts.WorkManagement.Projects;

public sealed record ProjectDetailViewModel(
    Guid Id, string Name, string Identifier, Guid CategoryId, string? Description,
    Guid LeadId, DateOnly StartDate, DateOnly TargetDate, string? Color,
    decimal? ActualHours, decimal AllocatedHours, decimal CompletedHours,
    bool IsActive, bool IsAchieved, DateTimeOffset? AchievedAt,
    DateTimeOffset CreatedAt, DateTimeOffset? UpdatedAt, bool IsLead);
