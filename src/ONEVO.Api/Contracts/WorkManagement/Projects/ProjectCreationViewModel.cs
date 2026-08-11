namespace ONEVO.Api.Contracts.WorkManagement.Projects;

public sealed record ProjectViewModel(
    Guid Id, string Name, string Identifier, Guid CategoryId, string? Description,
    Guid LeadId, DateOnly StartDate, DateOnly TargetDate, string? Color,
    decimal? ActualHours, decimal AllocatedHours, decimal CompletedHours,
    bool IsActive, DateTimeOffset CreatedAt);

public sealed record ObjectiveViewModel(
    Guid Id, Guid ProjectId, bool IsDefault, string Title, Guid OwnerId,
    DateOnly StartDate, DateOnly EndDate, decimal AllocatedHours, decimal CompletedHours);

public sealed record ProjectVersionViewModel(Guid Id, string Name, int StatusId, string StatusCode);

public sealed record ReleaseReminderViewModel(Guid Id, Guid VersionId, DateOnly ScheduledDate, string ReminderType);

public sealed record LabelViewModel(Guid Id, string Name, string Color);

public sealed record ProjectMembershipViewModel(Guid Id, Guid ObjectiveId, Guid UserId, string MembershipSource);

public sealed record ProjectLogoViewModel(Guid FileRecordId, string OriginalFileName);

public sealed record ProjectCreationViewModel(
    ProjectViewModel Project,
    ObjectiveViewModel DefaultObjective,
    ProjectVersionViewModel DefaultVersion,
    ReleaseReminderViewModel ReleaseReminder,
    IReadOnlyList<LabelViewModel> Labels,
    ProjectMembershipViewModel CreatorMembership,
    ProjectLogoViewModel? Logo);
