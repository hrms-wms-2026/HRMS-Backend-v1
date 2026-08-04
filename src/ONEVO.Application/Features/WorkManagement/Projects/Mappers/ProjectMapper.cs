using ONEVO.Application.Features.WorkManagement.Projects.DTOs.Responses;
using ONEVO.Domain.Features.WorkManagement.Labels.Entities;
using ONEVO.Domain.Features.WorkManagement.Objectives.Entities;
using ONEVO.Domain.Features.WorkManagement.ProjectMembers.Entities;
using ONEVO.Domain.Features.WorkManagement.Projects.Entities;
using ONEVO.Domain.Features.WorkManagement.ReleaseCalendar.Entities;
using ONEVO.Domain.Features.WorkManagement.Versions.Entities;

namespace ONEVO.Application.Features.WorkManagement.Projects.Mappers;

public static class ProjectMapper
{
    public static ProjectSummaryDto ToSummary(Project project) => new(
        project.Id, project.Name, project.Identifier, project.CategoryId, project.Description,
        project.LeadId, project.StartDate, project.TargetDate, project.Color,
        project.ActualHours, project.AllocatedHours, project.CompletedHours,
        project.IsActive, project.CreatedAt);

    public static ObjectiveSummaryDto ToSummary(Objective objective) => new(
        objective.Id, objective.ProjectId, objective.IsDefault, objective.Title, objective.OwnerId,
        objective.StartDate, objective.EndDate, objective.AllocatedHours, objective.CompletedHours);

    public static ProjectVersionSummaryDto ToSummary(ProjectVersion version, string statusCode) => new(
        version.Id, version.Name, version.StatusId, statusCode);

    public static ReleaseReminderSummaryDto ToSummary(ReleaseCalendarEntry entry) => new(
        entry.Id, entry.VersionId, entry.ScheduledDate, entry.ReminderType);

    public static LabelSummaryDto ToSummary(Label label) => new(label.Id, label.Name, label.Color);

    public static ProjectMembershipSummaryDto ToSummary(ProjectMember member) => new(
        member.Id, member.ObjectiveId, member.UserId, member.MembershipSource);

    public static ProjectDetailResponse ToDetail(Project project, bool isLead) => new(
        project.Id, project.Name, project.Identifier, project.CategoryId, project.Description,
        project.LeadId, project.StartDate, project.TargetDate, project.Color,
        project.ActualHours, project.AllocatedHours, project.CompletedHours,
        project.IsActive, project.CreatedAt, project.UpdatedAt, isLead);

    public static ProjectListItemResponse ToListItem(Project project, bool isLead) => new(
        project.Id, project.Name, project.Identifier, project.CategoryId, project.LeadId,
        project.StartDate, project.TargetDate, project.Color, project.IsActive,
        project.AllocatedHours, project.CompletedHours, isLead);
}
