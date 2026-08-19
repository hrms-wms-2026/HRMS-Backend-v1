namespace ONEVO.Api.Contracts.WorkManagement.Objectives;

public sealed record RequestAllocationExtensionRequest(decimal RequestedAdditionalHours, string Reason);
