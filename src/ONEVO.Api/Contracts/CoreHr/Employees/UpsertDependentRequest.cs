namespace ONEVO.Api.Contracts.CoreHr.Employees;

public record UpsertDependentRequest(string Name, string Relationship, DateOnly DateOfBirth, bool IsEmergencyContact, string? Phone);
