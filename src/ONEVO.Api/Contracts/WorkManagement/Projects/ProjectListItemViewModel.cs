namespace ONEVO.Api.Contracts.WorkManagement.Projects;

public sealed record ProjectMemberAvatarViewModel(Guid UserId, string DisplayName);

public sealed record ProjectListItemViewModel(
    Guid Id, string Name, string Identifier, Guid CategoryId, string? Description, Guid LeadId,
    DateOnly StartDate, DateOnly TargetDate, string? Color, bool IsActive,
    decimal AllocatedHours, decimal CompletedHours, bool IsLead,
    bool IsAchieved, DateTimeOffset? AchievedAt, DateTimeOffset? UpdatedAt, Guid? LogoFileId,
    IReadOnlyList<LabelViewModel> Labels, IReadOnlyList<ProjectMemberAvatarViewModel> Members, int MemberCount);
