namespace ONEVO.Application.Common.ServiceInterfaces;

public enum SeatDecisionStatus
{
    /// <summary>A purchased-seat count exists, capacity was computed, and a seat is available.</summary>
    Approved,

    /// <summary>A purchased-seat count exists and capacity was computed, but no seat remains
    /// and tenant billing policy does not permit overage.</summary>
    Blocked,

    /// <summary>No authoritative purchased-seat source exists on this tenant's subscription,
    /// so seat availability cannot be computed. Never inferred from CompanySizeRange or any
    /// other proxy. Always routes onboarding to Draft.</summary>
    Undetermined,
}

public record SeatDecision(
    SeatDecisionStatus Status,
    int? PurchasedSeats,
    int ActiveEmployeeCount,
    int PendingReservedSeats,
    int? AvailableSeats,
    bool OverageAllowed,
    bool RequestSeatIncreaseAvailable,
    string Reason);

public interface ISeatEntitlementService
{
    Task<SeatDecision> EvaluateAsync(Guid tenantId, CancellationToken ct = default);
}
