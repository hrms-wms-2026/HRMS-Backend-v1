using MonitoringException = ONEVO.Domain.Features.Monitoring.Exceptions.Entities.Exception;

namespace ONEVO.Application.Features.Monitoring.Exceptions.ServiceInterfaces;

/// <summary>
/// Sends the in-app notifications for an exception case. Notification rows are only added to the
/// unit of work - the caller owns SaveChanges, so the alert and its case commit together.
/// </summary>
public interface IExceptionAlertRouter
{
    /// <summary>A new case: the employee's reporting manager, or HR when nobody above them can review.</summary>
    Task NotifyDetectedAsync(MonitoringException exception, CancellationToken ct);

    /// <summary>A case that was escalated (manually or by the nightly sweep): HR.</summary>
    Task NotifyEscalatedAsync(MonitoringException exception, CancellationToken ct);
}

/// <summary>
/// Builds a router for one tenant from a background job. IEmployeeAuthorityResolver reads the
/// tenant from ICurrentUser, and a background scope has no signed-in user, so the router has to
/// be built around a fixed-tenant user instead of the request-scoped one.
/// </summary>
public interface IExceptionAlertRouterFactory
{
    IExceptionAlertRouter CreateForTenant(Guid tenantId);
}
