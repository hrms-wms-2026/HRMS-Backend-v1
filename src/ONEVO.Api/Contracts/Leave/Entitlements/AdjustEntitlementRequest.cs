namespace ONEVO.Api.Contracts.Leave.Entitlements;

public record AdjustEntitlementRequest(
    decimal TotalHours,
    decimal CarriedForwardHours,
    string Reason,
    bool ConfirmNegativeRemaining);
