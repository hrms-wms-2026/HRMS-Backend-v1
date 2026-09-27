namespace ONEVO.Api.Contracts.WorkManagement.Objectives;

public sealed record ObjectiveChangeRequestViewModel(
    Guid Id, Guid ObjectiveId, string RequestType, Guid RequestedById, Guid ReportingManagerId,
    string Status, string? PayloadJson, DateTimeOffset? DecidedAt, Guid? DecidedById, DateTimeOffset CreatedAt,
    string? RequestedByName, string? ObjectiveTitle, Guid? ProjectId, decimal? CurrentAllocatedHours);
