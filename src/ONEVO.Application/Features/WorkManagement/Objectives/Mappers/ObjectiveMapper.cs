using ONEVO.Application.Features.WorkManagement.ObjectiveChangeRequests.DTOs.Responses;
using ONEVO.Application.Features.WorkManagement.Objectives.DTOs.Responses;
using ONEVO.Domain.Features.WorkManagement.ObjectiveChangeRequests.Entities;
using ONEVO.Domain.Features.WorkManagement.Objectives.Entities;

namespace ONEVO.Application.Features.WorkManagement.Objectives.Mappers;

public static class ObjectiveMapper
{
    public static ObjectiveDetailResponse ToDetail(
        Objective objective, IReadOnlyDictionary<Guid, string>? namesByEmployeeId = null, Guid? callerEmployeeId = null) => new(
        objective.Id, objective.ProjectId, objective.ParentObjectiveId, objective.IsDefault, objective.Title, objective.Description,
        objective.OwnerId, objective.ReportingManagerId, objective.CreatedById, objective.StartDate, objective.EndDate,
        objective.Progress, objective.ActualHours, objective.AllocatedHours, objective.CompletedHours,
        objective.IsActive, objective.IsAchieved, objective.AchievedAt, objective.CreatedAt, objective.UpdatedAt,
        ResolveName(objective.OwnerId, namesByEmployeeId), ResolveName(objective.ReportingManagerId, namesByEmployeeId),
        callerEmployeeId.HasValue && objective.OwnerId == callerEmployeeId.Value);

    private static string? ResolveName(Guid? employeeId, IReadOnlyDictionary<Guid, string>? namesByEmployeeId)
        => employeeId.HasValue && namesByEmployeeId is not null && namesByEmployeeId.TryGetValue(employeeId.Value, out var name) ? name : null;

    public static ObjectiveTreeItemResponse ToTreeItem(Objective objective, bool isOwner = false, string? ownerName = null) => new(
        objective.Id, objective.ParentObjectiveId, objective.IsDefault, objective.Title, objective.OwnerId,
        objective.StartDate, objective.EndDate, objective.AllocatedHours, objective.CompletedHours, objective.IsActive, objective.IsAchieved,
        objective.Progress, ownerName, isOwner);

    public static ObjectiveSubtreeNodeResponse ToSubtreeNode(
        Objective objective, ILookup<Guid, Objective> childrenByParent,
        IReadOnlyDictionary<Guid, string>? namesByEmployeeId = null, Guid? callerEmployeeId = null) => new(
        objective.Id, objective.ProjectId, objective.ParentObjectiveId, objective.IsDefault, objective.Title, objective.Description,
        objective.OwnerId, objective.ReportingManagerId, objective.CreatedById, objective.StartDate, objective.EndDate,
        objective.Progress, objective.ActualHours, objective.AllocatedHours, objective.CompletedHours,
        objective.IsActive, objective.CreatedAt, objective.UpdatedAt,
        ResolveName(objective.OwnerId, namesByEmployeeId), ResolveName(objective.ReportingManagerId, namesByEmployeeId),
        callerEmployeeId.HasValue && objective.OwnerId == callerEmployeeId.Value,
        objective.IsAchieved, objective.AchievedAt,
        childrenByParent[objective.Id].Select(c => ToSubtreeNode(c, childrenByParent, namesByEmployeeId, callerEmployeeId)).ToList());

    public static ObjectiveChangeRequestResponse ToResponse(ObjectiveChangeRequest request) => new(
        request.Id, request.ObjectiveId, request.RequestType, request.RequestedById, request.ReportingManagerId,
        request.Status, request.PayloadJson, request.DecidedAt, request.DecidedById, request.CreatedAt);
}
