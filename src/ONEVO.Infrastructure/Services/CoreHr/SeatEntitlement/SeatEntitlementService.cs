using Microsoft.EntityFrameworkCore;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Domain.Features.CoreHr.Entities;
using ONEVO.Domain.Features.SharedPlatform.Entities;
using ONEVO.Infrastructure.Persistence;

namespace ONEVO.Infrastructure.Services.CoreHr.SeatEntitlement;

/// <summary>
/// Reports whether a seat is available for a new employee. There is no numeric
/// purchased-seat field anywhere in TenantSubscription or SubscriptionPlan today -
/// CompanySizeRange is a string bracket ("51-200") consumed only by the storage-quota
/// calculator, and FeatureLimitsJson exists but is never populated by any seeder or handler
/// (verified by inspection before writing this service). This service therefore always
/// returns Undetermined rather than inferring a number from either field. See
/// EMPLOYEE_MANAGEMENT_IMPLEMENTATION_REPORT.md for the missing product/backend decision
/// this blocks on.
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

        var pendingReservedSeats = await _db.OnboardingDrafts.AsNoTracking()
            .CountAsync(d => d.TenantId == tenantId && d.Status == OnboardingDraftStatus.WaitingForSeat, ct);

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

        return new SeatDecision(
            SeatDecisionStatus.Undetermined,
            PurchasedSeats: null,
            activeEmployeeCount,
            pendingReservedSeats,
            AvailableSeats: null,
            OverageAllowed: false,
            RequestSeatIncreaseAvailable: true,
            Reason: "No purchased-seat count is configured on this tenant's subscription. A product " +
                "decision is required before seat availability can be computed - see " +
                "EMPLOYEE_MANAGEMENT_IMPLEMENTATION_REPORT.md.");
    }
}
