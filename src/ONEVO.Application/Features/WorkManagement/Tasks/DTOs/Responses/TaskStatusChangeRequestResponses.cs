using System.Text.Json;
using ONEVO.Domain.Features.WorkManagement.Approvals.Entities;

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
    // Case-insensitive: rows migrated from task_status_change_requests were serialised PascalCase.
    private static readonly JsonSerializerOptions PayloadOptions = new(JsonSerializerDefaults.Web);

    /// <summary>Maps a project.status_template_change row of wm_approval_requests.</summary>
    public static TaskStatusChangeRequestResponse From(
        WorkApprovalRequest request, string requesterDisplayName, bool canDecide, bool canCancel)
    {
        var payload = JsonSerializer.Deserialize<TaskStatusTemplateChangePayload>(request.PayloadJson, PayloadOptions)!;
        return new(
            request.Id, request.ProjectId, request.Status, request.RequestedByEmployeeId, requesterDisplayName,
            payload.Note, payload.Changes, request.CreatedAt, request.DecidedByEmployeeId, request.DecisionComment,
            request.DecidedAt, canDecide, canCancel);
    }
}

/// <summary>The caller's standing over a project's status template, plus the pending requests they
/// can see: approvers see every pending request, requesters see only their own.</summary>
public sealed record ProjectTaskStatusChangeRequestsResponse(
    bool CanEditDirectly,
    bool CanRequest,
    IReadOnlyList<TaskStatusChangeRequestResponse> Requests);
