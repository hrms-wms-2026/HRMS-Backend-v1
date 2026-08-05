using ONEVO.Application.Features.WorkManagement.ObjectiveChangeRequests.DTOs.Responses;
using ONEVO.Application.Features.WorkManagement.Objectives.DTOs.Responses;

namespace ONEVO.Api.Contracts.WorkManagement.Objectives;

public static class ObjectiveViewModelMapper
{
    public static ObjectiveDetailViewModel ToViewModel(this ObjectiveDetailResponse dto) => new(
        dto.Id, dto.ProjectId, dto.ParentObjectiveId, dto.IsDefault, dto.Title, dto.Description,
        dto.OwnerId, dto.ReportingManagerId, dto.CreatedById, dto.StartDate, dto.EndDate,
        dto.Progress, dto.ActualHours, dto.AllocatedHours, dto.CompletedHours,
        dto.IsActive, dto.CreatedAt, dto.UpdatedAt);

    public static ObjectiveTreeItemViewModel ToViewModel(this ObjectiveTreeItemResponse dto) => new(
        dto.Id, dto.ParentObjectiveId, dto.IsDefault, dto.Title, dto.OwnerId,
        dto.StartDate, dto.EndDate, dto.AllocatedHours, dto.CompletedHours, dto.IsActive);

    public static ObjectiveChangeRequestViewModel ToViewModel(this ObjectiveChangeRequestResponse dto) => new(
        dto.Id, dto.ObjectiveId, dto.RequestType, dto.RequestedById, dto.ReportingManagerId,
        dto.Status, dto.PayloadJson, dto.DecidedAt, dto.DecidedById, dto.CreatedAt);
}
