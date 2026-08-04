using ONEVO.Api.Contracts.Common;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Features.WorkManagement.Projects.DTOs.Responses;

namespace ONEVO.Api.Contracts.WorkManagement.Projects;

public static class ProjectViewModelMapper
{
    public static ProjectCreationViewModel ToViewModel(this ProjectCreationResponse response) => new(
        response.Project.ToViewModel(),
        response.DefaultObjective.ToViewModel(),
        response.DefaultVersion.ToViewModel(),
        response.ReleaseReminder.ToViewModel(),
        response.Labels.Select(ToViewModel).ToList(),
        response.CreatorMembership.ToViewModel(),
        response.Logo?.ToViewModel()
    );

    public static ProjectViewModel ToViewModel(this ProjectSummaryDto dto) => new(
        dto.Id, dto.Name, dto.Identifier, dto.CategoryId, dto.Description,
        dto.LeadId, dto.StartDate, dto.TargetDate, dto.Color,
        dto.ActualHours, dto.AllocatedHours, dto.CompletedHours,
        dto.IsActive, dto.CreatedAt);

    public static ObjectiveViewModel ToViewModel(this ObjectiveSummaryDto dto) => new(
        dto.Id, dto.ProjectId, dto.IsDefault, dto.Title, dto.OwnerId,
        dto.StartDate, dto.EndDate, dto.AllocatedHours, dto.CompletedHours);

    public static ProjectVersionViewModel ToViewModel(this ProjectVersionSummaryDto dto) => new(
        dto.Id, dto.Name, dto.StatusId, dto.StatusCode);

    public static ReleaseReminderViewModel ToViewModel(this ReleaseReminderSummaryDto dto) => new(
        dto.Id, dto.VersionId, dto.ScheduledDate, dto.ReminderType);

    public static LabelViewModel ToViewModel(this LabelSummaryDto dto) => new(dto.Id, dto.Name, dto.Color);

    public static ProjectMembershipViewModel ToViewModel(this ProjectMembershipSummaryDto dto) => new(
        dto.Id, dto.ObjectiveId, dto.UserId, dto.MembershipSource);

    public static ProjectLogoViewModel ToViewModel(this ProjectLogoSummaryDto dto) => new(
        dto.FileRecordId, dto.OriginalFileName);

    public static ProjectDetailViewModel ToViewModel(this ProjectDetailResponse dto) => new(
        dto.Id, dto.Name, dto.Identifier, dto.CategoryId, dto.Description,
        dto.LeadId, dto.StartDate, dto.TargetDate, dto.Color,
        dto.ActualHours, dto.AllocatedHours, dto.CompletedHours,
        dto.IsActive, dto.CreatedAt, dto.UpdatedAt, dto.IsLead);

    public static ProjectListItemViewModel ToViewModel(this ProjectListItemResponse dto) => new(
        dto.Id, dto.Name, dto.Identifier, dto.CategoryId, dto.LeadId,
        dto.StartDate, dto.TargetDate, dto.Color, dto.IsActive,
        dto.AllocatedHours, dto.CompletedHours, dto.IsLead);

    public static PagedResultViewModel<ProjectListItemViewModel> ToViewModel(this PagedResult<ProjectListItemResponse> page) => new(
        page.Items.Select(ToViewModel).ToList(), page.PageNumber, page.PageSize, page.TotalCount, page.TotalPages, page.HasNext, page.HasPrevious);
}
