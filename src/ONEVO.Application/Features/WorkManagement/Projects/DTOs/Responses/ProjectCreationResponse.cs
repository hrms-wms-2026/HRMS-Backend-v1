namespace ONEVO.Application.Features.WorkManagement.Projects.DTOs.Responses;

public sealed record ProjectSummaryDto(
    Guid Id, string Name, string Identifier, Guid CategoryId, string? Description,
    Guid LeadId, DateOnly StartDate, DateOnly TargetDate, string? Color,
    decimal? ActualHours, decimal AllocatedHours, decimal CompletedHours,
    bool IsActive, DateTimeOffset CreatedAt);

public sealed record ObjectiveSummaryDto(
    Guid Id, Guid ProjectId, bool IsDefault, string Title, Guid OwnerId,
    DateOnly StartDate, DateOnly EndDate, decimal AllocatedHours, decimal CompletedHours);

public sealed record ProjectVersionSummaryDto(Guid Id, string Name, int StatusId, string StatusCode);

public sealed record ReleaseReminderSummaryDto(Guid Id, Guid VersionId, DateOnly ScheduledDate, string ReminderType);

public sealed record LabelSummaryDto(Guid Id, string Name, string Color);

public sealed record ProjectMembershipSummaryDto(Guid Id, Guid ObjectiveId, Guid UserId, string MembershipSource);

public sealed record ProjectLogoSummaryDto(Guid FileRecordId, string OriginalFileName);

public sealed record ProjectCreationResponse(
    ProjectSummaryDto Project,
    ObjectiveSummaryDto DefaultObjective,
    ProjectVersionSummaryDto DefaultVersion,
    ReleaseReminderSummaryDto ReleaseReminder,
    IReadOnlyList<LabelSummaryDto> Labels,
    ProjectMembershipSummaryDto CreatorMembership,
    ProjectLogoSummaryDto? Logo,
    ProjectLogoSummaryDto? Banner);
