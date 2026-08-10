using ONEVO.Application.Features.WorkManagement.ObjectiveChangeRequests.DTOs.Responses;
using ONEVO.Application.Features.WorkManagement.Objectives.DTOs.Responses;
using ONEVO.Domain.Features.WorkManagement.ObjectiveChangeRequests.Entities;
using ONEVO.Domain.Features.WorkManagement.Objectives.Entities;

namespace ONEVO.Application.Features.WorkManagement.Objectives.Mappers;

public static class ObjectiveMapper
{
    public static ObjectiveDetailResponse ToDetail(
        Objective objective, IReadOnlyDictionary<Guid, string>? namesByUserId = null, Guid? currentUserId = null) => new(
        objective.Id, objective.ProjectId, objective.ParentObjectiveId, objective.IsDefault, objective.Title, objective.Description,
        objective.OwnerId, objective.ReportingManagerId, objective.CreatedById, objective.StartDate, objective.EndDate,
        objective.Progress, objective.ActualHours, objective.AllocatedHours, objective.CompletedHours,
        objective.IsActive, objective.IsAchieved, objective.AchievedAt, objective.CreatedAt, objective.UpdatedAt,
        ResolveName(objective.OwnerId, namesByUserId), ResolveName(objective.ReportingManagerId, namesByUserId),
        currentUserId.HasValue && objective.OwnerId == currentUserId.Value);

    private static string? ResolveName(Guid? userId, IReadOnlyDictionary<Guid, string>? namesByUserId)
        => userId.HasValue && namesByUserId is not null && namesByUserId.TryGetValue(userId.Value, out var name) ? name : null;

    public static ObjectiveTreeItemResponse ToTreeItem(Objective objective) => new(
        objective.Id, objective.ParentObjectiveId, objective.IsDefault, objective.Title, objective.OwnerId,
        objective.StartDate, objective.EndDate, objective.AllocatedHours, objective.CompletedHours, objective.IsActive, objective.IsAchieved);

    public static ObjectiveSubtreeNodeResponse ToSubtreeNode(
        Objective objective, ILookup<Guid, Objective> childrenByParent,
        IReadOnlyDictionary<Guid, string>? namesByUserId = null, Guid? currentUserId = null) => new(
        objective.Id, objective.ProjectId, objective.ParentObjectiveId, objective.IsDefault, objective.Title, objective.Description,
        objective.OwnerId, objective.ReportingManagerId, objective.CreatedById, objective.StartDate, objective.EndDate,
        objective.Progress, objective.ActualHours, objective.AllocatedHours, objective.CompletedHours,
        objective.IsActive, objective.CreatedAt, objective.UpdatedAt,
        ResolveName(objective.OwnerId, namesByUserId), ResolveName(objective.ReportingManagerId, namesByUserId),
        currentUserId.HasValue && objective.OwnerId == currentUserId.Value,
        objective.IsAchieved, objective.AchievedAt,
        childrenByParent[objective.Id].Select(c => ToSubtreeNode(c, childrenByParent, namesByUserId, currentUserId)).ToList());

    public static ObjectiveChangeRequestResponse ToResponse(ObjectiveChangeRequest request) => new(
        request.Id, request.ObjectiveId, request.RequestType, request.RequestedById, request.ReportingManagerId,
        request.Status, request.PayloadJson, request.DecidedAt, request.DecidedById, request.CreatedAt);
}
