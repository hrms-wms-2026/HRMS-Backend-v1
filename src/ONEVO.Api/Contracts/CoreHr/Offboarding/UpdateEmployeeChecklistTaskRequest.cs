namespace ONEVO.Api.Contracts.CoreHr.Offboarding;

public sealed record UpdateEmployeeChecklistTaskRequest(Guid AssignedToId, DateOnly DueDate, bool IsRequired);
