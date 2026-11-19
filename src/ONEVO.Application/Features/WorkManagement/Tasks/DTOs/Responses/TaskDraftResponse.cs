namespace ONEVO.Application.Features.WorkManagement.Tasks.DTOs.Responses;

public sealed record TaskDraftSummaryResponse(Guid Id, Guid ProjectId, string Title, DateTimeOffset UpdatedAt);

public sealed record TaskDraftResponse(Guid Id, Guid ProjectId, string Title, string PayloadJson, DateTimeOffset UpdatedAt);
