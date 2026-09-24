using Microsoft.EntityFrameworkCore;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Domain.Features.CoreHr.Entities;
using ONEVO.Domain.Features.SharedPlatform.Entities;
using ONEVO.Infrastructure.Persistence;

namespace ONEVO.Infrastructure.Services.CoreHr.SeatEntitlement;

/// <summary>
/// Reports whether a tenant-wide employee seat is available from the authoritative
/// subscription policy. It intentionally never infers capacity from company size or
/// organisational data. Waiting drafts are not reservations: a blocked draft has not
/// acquired a billable seat, and there is no reservation lifecycle in the model.
/// </summary>
public class SeatEntitlementService : ISeatEntitlementService
{
    private readonly ApplicationDbContext _db;

    public SeatEntitlementService(ApplicationDbContext db)
    {
        _db = db;
    }

    public async Task<SeatDecision> EvaluateAsync(Guid tenantId, CancellationToken ct = default)
    {
        var subscription = await _db.Set<TenantSubscription>()
            .AsNoTracking()
            .FirstOrDefaultAsync(s => s.TenantId == tenantId, ct);

        var activeEmployeeCount = await _db.Employees.AsNoTracking().CountAsync(e => e.TenantId == tenantId, ct);

        const int pendingReservedSeats = 0;

        if (subscription is null)
        {
            return new SeatDecision(
                SeatDecisionStatus.Undetermined,
                PurchasedSeats: null,
                activeEmployeeCount,
                pendingReservedSeats,
                AvailableSeats: null,
                OverageAllowed: false,
                RequestSeatIncreaseAvailable: false,
                Reason: "No tenant subscription record found; seat entitlement cannot be evaluated.");
        }

        if (subscription.IncludedSeats is null || subscription.IncludedSeats < 0 || subscription.OverageAllowed is null)
        {
            return new SeatDecision(
            SeatDecisionStatus.Undetermined,
            PurchasedSeats: subscription.IncludedSeats,
            activeEmployeeCount,
            pendingReservedSeats,
            AvailableSeats: null,
            OverageAllowed: subscription.OverageAllowed ?? false,
            RequestSeatIncreaseAvailable: true,
            Reason: "Tenant subscription seat policy is missing or invalid.");
        }

        var availableSeats = subscription.IncludedSeats.Value - activeEmployeeCount;
        if (availableSeats > 0 || subscription.OverageAllowed.Value)
        {
            return new SeatDecision(SeatDecisionStatus.Approved, subscription.IncludedSeats,
                activeEmployeeCount, pendingReservedSeats, Math.Max(availableSeats, 0),
                subscription.OverageAllowed.Value, false,
                availableSeats > 0 ? "Included seat capacity is available." : "Overage is allowed by this tenant's subscription.");
        }

        return new SeatDecision(SeatDecisionStatus.Blocked, subscription.IncludedSeats,
            activeEmployeeCount, pendingReservedSeats, 0, false, true,
            "All included seats are in use and overage is disabled.");
    }
}
