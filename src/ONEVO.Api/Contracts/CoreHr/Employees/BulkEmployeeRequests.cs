namespace ONEVO.Api.Contracts.CoreHr.Employees;

public sealed record BulkChangePositionRequest(
    IReadOnlyList<Guid> EmployeeIds, Guid PositionId, DateOnly EffectiveFrom, string ChangeReason, Guid? ReportsToEmployeeId = null);

public sealed record BulkChangeEmploymentTypeRequest(IReadOnlyList<Guid> EmployeeIds, string EmploymentTypeCode);

public sealed record BulkStartOffboardingRequest(
    IReadOnlyList<Guid> EmployeeIds, string Reason, DateOnly LastWorkingDate, string KnowledgeRiskLevel, string? RehireEligibility, string? Notes);
