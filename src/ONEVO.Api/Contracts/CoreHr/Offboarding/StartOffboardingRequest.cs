namespace ONEVO.Api.Contracts.CoreHr.Offboarding;

public sealed record StartOffboardingRequest(
    string Reason, DateOnly LastWorkingDate, string KnowledgeRiskLevel, string? RehireEligibility, string? Notes);
