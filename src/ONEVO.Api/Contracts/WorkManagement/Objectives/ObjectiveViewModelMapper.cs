using ONEVO.Api.Contracts.WorkManagement.ProjectInvitations;
using ONEVO.Application.Features.WorkManagement.ObjectiveChangeRequests.DTOs.Responses;
using ONEVO.Application.Features.WorkManagement.Objectives.DTOs.Responses;

namespace ONEVO.Api.Contracts.WorkManagement.Objectives;

public static class ObjectiveViewModelMapper
{
    public static ObjectiveDetailViewModel ToViewModel(this ObjectiveDetailResponse dto) => new(
        dto.Id, dto.ProjectId, dto.ParentObjectiveId, dto.IsDefault, dto.Title, dto.Description,
        dto.OwnerId, dto.ReportingManagerId, dto.CreatedById, dto.StartDate, dto.EndDate,
        dto.Progress, dto.ActualHours, dto.AllocatedHours, dto.CompletedHours,
        dto.IsActive, dto.IsAchieved, dto.AchievedAt, dto.CreatedAt, dto.UpdatedAt,
        dto.OwnerName, dto.ReportingManagerName, dto.IsOwner);

    public static ObjectiveTreeItemViewModel ToViewModel(this ObjectiveTreeItemResponse dto) => new(
        dto.Id, dto.ParentObjectiveId, dto.IsDefault, dto.Title, dto.OwnerId,
        dto.StartDate, dto.EndDate, dto.AllocatedHours, dto.CompletedHours, dto.IsActive, dto.IsAchieved,
        dto.Progress, dto.OwnerName, dto.IsOwner);

    public static ObjectiveChangeRequestViewModel ToViewModel(this ObjectiveChangeRequestResponse dto) => new(
        dto.Id, dto.ObjectiveId, dto.RequestType, dto.RequestedById, dto.ReportingManagerId,
        dto.Status, dto.PayloadJson, dto.DecidedAt, dto.DecidedById, dto.CreatedAt);

    public static ObjectiveSubtreeViewModel ToViewModel(this ObjectiveSubtreeResponse dto) => new(
        dto.ParentObjective?.ToViewModel(), dto.Objective.ToViewModel());

    public static ObjectiveSubtreeNodeViewModel ToViewModel(this ObjectiveSubtreeNodeResponse dto) => new(
        dto.Id, dto.ProjectId, dto.ParentObjectiveId, dto.IsDefault, dto.Title, dto.Description,
        dto.OwnerId, dto.ReportingManagerId, dto.CreatedById, dto.StartDate, dto.EndDate,
        dto.Progress, dto.ActualHours, dto.AllocatedHours, dto.CompletedHours,
        dto.IsActive, dto.CreatedAt, dto.UpdatedAt,
        dto.OwnerName, dto.ReportingManagerName, dto.IsOwner, dto.IsAchieved, dto.AchievedAt,
        dto.Children.Select(c => c.ToViewModel()).ToList());

    public static ObjectiveHistoryItemViewModel ToViewModel(this ObjectiveHistoryItemResponse dto) => new(
        dto.ObjectiveId, dto.Title, dto.ProjectId, dto.IsAchieved, dto.RemovedAt);

    public static MyProjectMilestoneViewModel ToViewModel(this MyProjectMilestoneResponse dto) => new(
        dto.ObjectiveId, dto.ProjectId, dto.ParentObjectiveId, dto.IsDefault, dto.Title,
        dto.OwnerId, dto.OwnerName, dto.ReportingManagerId, dto.ReportingManagerName,
        dto.StartDate, dto.EndDate, dto.AllocatedHours, dto.CompletedHours,
        dto.ObjectiveIsActive, dto.IsAchieved, dto.AchievedAt,
        dto.MembershipIsActive, dto.MembershipRemovedAt, dto.IsOwner);

    public static AddObjectiveMemberOutcomeViewModel ToViewModel(this AddObjectiveMemberOutcomeResponse dto) => new()
    {
        AlreadyMember = dto.AlreadyMember,
        Invitation = dto.Invitation?.ToViewModel()
    };

    public static ObjectiveMemberListViewModel ToViewModel(this ObjectiveMemberListResponse response) => new()
    {
        Items = response.Items.Select(i => new ObjectiveMemberItemViewModel
        {
            EmployeeId = i.EmployeeId, IsHead = i.IsHead, Pending = i.Pending,
            InviteType = i.InviteType, InvitationId = i.InvitationId, SinceOrInvitedAt = i.SinceOrInvitedAt
        }).ToList()
    };

    public static TransferOutcomeViewModel ToViewModel(this TransferOutcomeResponse dto) => new()
    {
        Applied = dto.Applied,
        PendingChangeRequest = dto.PendingChangeRequest?.ToViewModel(),
        PendingInvitation = dto.PendingInvitation?.ToViewModel()
    };
}
