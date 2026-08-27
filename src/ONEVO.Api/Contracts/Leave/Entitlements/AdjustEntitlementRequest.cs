namespace ONEVO.Api.Contracts.Leave.Entitlements;

public record AdjustEntitlementRequest(
    decimal TotalDays,
    decimal CarriedForwardDays,
    string Reason,
    bool ConfirmNegativeRemaining);
