namespace ONEVO.Api.Contracts.CoreHr.Employees;

public record UpsertEmergencyContactRequest(string Name, string Relationship, string Phone, string? Email, bool IsPrimary);
