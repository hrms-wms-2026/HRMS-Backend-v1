using System.Text.Json;
using ONEVO.Domain.Features.WorkManagement.Tasks.Entities;

namespace ONEVO.Application.Features.WorkManagement.Tasks.DTOs.Responses;

public sealed record TaskStatusChangeRequestResponse(
    Guid Id,
    Guid ProjectId,
    string Status,
    Guid RequestedByEmployeeId,
    string RequesterDisplayName,
    string? Note,
    TaskStatusChangeSet Changes,
    DateTimeOffset CreatedAt,
    Guid? DecidedByEmployeeId,
    string? DecisionComment,
    DateTimeOffset? DecidedAt,
    bool CanDecide,
    bool CanCancel)
{
    public static TaskStatusChangeRequestResponse From(
        TaskStatusChangeRequest entity, string requesterDisplayName, bool canDecide, bool canCancel)
        => new(
            entity.Id, entity.ProjectId, entity.Status, entity.RequestedByEmployeeId, requesterDisplayName,
            entity.Note, JsonSerializer.Deserialize<TaskStatusChangeSet>(entity.ChangesJson)!,
            entity.CreatedAt, entity.DecidedByEmployeeId, entity.DecisionComment, entity.DecidedAt,
            canDecide, canCancel);
}

/// <summary>The caller's standing over a project's status template, plus the pending requests they
/// can see: approvers see every pending request, requesters see only their own.</summary>
public sealed record ProjectTaskStatusChangeRequestsResponse(
    bool CanEditDirectly,
    bool CanRequest,
    IReadOnlyList<TaskStatusChangeRequestResponse> Requests);
