namespace ONEVO.Application.Features.Monitoring.CheckIn.ServiceInterfaces;

/// <summary>
/// Resolves the real CoreHR Employee.Id for a tray device's JWT-bound UserId, for handlers that
/// persist an EmployeeId onto a domain entity (raw monitoring snapshots, biometric profiles,
/// evidence assets, work-location confirmations, notifications). Falls back to the raw UserId
/// when no Employee row exists yet (tray devices can activate before CoreHR onboarding creates
/// one - see TrayEnrollmentService.ResolveEmployeeIdentityAsync's "profile_unavailable" status),
/// so a pre-onboarding device still gets its data recorded rather than rejected; that data is
/// only correctly attributable once the Employee row exists and the historical backfill re-runs.
///
/// Deliberately separate from IMonitoringToggleResolver: that resolver's employeeId/userId
/// parameters intentionally want the raw UserId (a user may hold more than one Employee row for
/// a multi-company user, and toggle resolution defers to the same "default employee for this
/// user" logic regardless of which Employee ends up owning the persisted row). Call sites that
/// only need a capability/threshold check must keep passing the raw UserId to that resolver -
/// only the value that gets stored on a persisted entity should go through this resolver.
/// </summary>
public interface ITrayEmployeeIdentityResolver
{
    Task<Guid> ResolveEmployeeIdAsync(
        Guid tenantId, Guid userId, Guid? legalEntityId, CancellationToken ct = default);
}
