namespace ONEVO.Application.Features.CoreHr.Offboarding.Queries.GetOffboarding;

public sealed record OffboardingRecordResponse(
    Guid Id, Guid EmployeeId, string Reason, DateOnly LastWorkingDate, string KnowledgeRiskLevel,
    string? RehireEligibility, string? Notes, Guid? ChecklistTemplateId, string Status,
    DateTimeOffset CreatedAt, DateTimeOffset? UpdatedAt, DateTimeOffset? CompletedAt);
