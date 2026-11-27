namespace ONEVO.Application.Features.WorkManagement.Projects.DTOs.Responses;

public sealed record ProjectMemberAvatarDto(Guid UserId, string DisplayName, Guid? AvatarFileId);

public sealed record ProjectListItemResponse(
    Guid Id, string Name, string Identifier, Guid CategoryId, string? Description, Guid LeadId,
    DateOnly StartDate, DateOnly TargetDate, string? Color, bool IsActive,
    decimal AllocatedHours, decimal CompletedHours, bool IsLead,
    bool IsAchieved, DateTimeOffset? AchievedAt, DateTimeOffset? UpdatedAt, Guid? LogoFileId,
    IReadOnlyList<LabelSummaryDto> Labels, IReadOnlyList<ProjectMemberAvatarDto> Members, int MemberCount,
    string? SystemPurpose = null, string? Icon = null,
    // The listed employee's own tasks in this project: estimated hours of all of them, of the open ones, and the open count.
    decimal MyAllocatedHours = 0m, decimal MyOpenHours = 0m, int MyOpenTaskCount = 0);
