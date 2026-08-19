namespace ONEVO.Application.Features.CoreHr.Offboarding.Queries.ListMyPendingBypassRequests;

public sealed record BypassRequestResponse(
    Guid Id, Guid EmployeeChecklistTaskId, Guid OffboardingRecordId, Guid RequestedById,
    string BypassReason, string? PenaltyDescription, string Status, DateTimeOffset RequestedAt);
