namespace ONEVO.Application.Features.WorkManagement.Projects.DTOs.Responses;

public sealed record ProjectDetailResponse(
    Guid Id, string Name, string Identifier, Guid CategoryId, string? Description,
    Guid LeadId, DateOnly StartDate, DateOnly TargetDate, string? Color,
    decimal? ActualHours, decimal AllocatedHours, decimal CompletedHours,
    bool IsActive, bool IsAchieved, DateTimeOffset? AchievedAt,
    DateTimeOffset CreatedAt, DateTimeOffset? UpdatedAt, bool IsLead, Guid? LogoFileId,
    IReadOnlyList<LabelSummaryDto> Labels, IReadOnlyList<ProjectMemberAvatarDto> Members, int MemberCount);
