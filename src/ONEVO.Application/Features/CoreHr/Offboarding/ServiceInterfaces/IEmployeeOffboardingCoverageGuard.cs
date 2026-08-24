using ONEVO.Application.Common.Models;

namespace ONEVO.Application.Features.CoreHr.Offboarding.ServiceInterfaces;

/// <summary>Enforces the 2026-08-18 coverage requirement: a caller may only act on an employee's
/// offboarding record if that employee falls within the caller's own management_coverage_records
/// coverage - never bypassed by an "unrestricted" flag, unlike the rest of this app's employee
/// visibility (see design spec §11 for why that's a deliberate, stricter exception here).</summary>
public interface IEmployeeOffboardingCoverageGuard
{
    Task<Result?> EnsureCovered(Guid tenantId, Guid actingUserId, Guid targetEmployeeId, CancellationToken ct = default);
}
