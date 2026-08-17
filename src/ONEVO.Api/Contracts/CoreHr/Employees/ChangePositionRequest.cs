namespace ONEVO.Api.Contracts.CoreHr.Employees;

public sealed record ChangePositionRequest(Guid PositionId, DateOnly EffectiveFrom);
