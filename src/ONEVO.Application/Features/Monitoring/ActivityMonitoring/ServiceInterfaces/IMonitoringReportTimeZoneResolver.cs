namespace ONEVO.Application.Features.Monitoring.ActivityMonitoring.ServiceInterfaces;

/// <summary>
/// Resolves the IANA/Windows timezone for an employee's legal entity.
/// Falls back to UTC when the employee or timezone is missing or invalid.
/// </summary>
public interface IMonitoringReportTimeZoneResolver
{
    Task<TimeZoneInfo> ResolveAsync(
        Guid tenantId,
        Guid employeeId,
        CancellationToken ct);
}
