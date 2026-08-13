namespace ONEVO.Application.Features.WorkManagement.ObjectiveChangeRequests.DTOs.Responses;

public sealed record ObjectiveChangeRequestResponse(
    Guid Id, Guid ObjectiveId, string RequestType, Guid RequestedById, Guid ReportingManagerId,
    string Status, string? PayloadJson, DateTimeOffset? DecidedAt, Guid? DecidedById, DateTimeOffset CreatedAt);
