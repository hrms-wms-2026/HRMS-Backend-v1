namespace ONEVO.Application.Features.WorkManagement.Projects.DTOs.Responses;

public sealed record ProjectListItemResponse(
    Guid Id, string Name, string Identifier, Guid CategoryId, Guid LeadId,
    DateOnly StartDate, DateOnly TargetDate, string? Color, bool IsActive,
    decimal AllocatedHours, decimal CompletedHours, bool IsLead,
    bool IsAchieved, DateTimeOffset? AchievedAt);
