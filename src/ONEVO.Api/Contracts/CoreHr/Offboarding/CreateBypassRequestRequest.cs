namespace ONEVO.Api.Contracts.CoreHr.Offboarding;

public sealed record CreateBypassRequestRequest(Guid ApproverId, string BypassReason, string? PenaltyDescription);
