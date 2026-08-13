using Microsoft.Extensions.Logging;
using ONEVO.Application.Features.Monitoring.ActivityMonitoring.ServiceInterfaces;
using ONEVO.Application.Features.OrgStructure.RepositoryInterfaces;
using CommonEmployeeRepository = ONEVO.Application.Common.RepositoryInterfaces.IEmployeeRepository;
using CoreHrEmployeeRepository = ONEVO.Application.Features.CoreHr.Employee.RepositoryInterfaces.IEmployeeRepository;

namespace ONEVO.Infrastructure.Services.Monitoring.ActivityMonitoring;

public sealed class MonitoringReportTimeZoneResolver : IMonitoringReportTimeZoneResolver
{
    private readonly CoreHrEmployeeRepository _coreHrEmployees;
    private readonly CommonEmployeeRepository _employees;
    private readonly ILegalEntityRepository _legalEntities;
    private readonly ILogger<MonitoringReportTimeZoneResolver> _logger;

    public MonitoringReportTimeZoneResolver(
        CoreHrEmployeeRepository coreHrEmployees,
        CommonEmployeeRepository employees,
        ILegalEntityRepository legalEntities,
        ILogger<MonitoringReportTimeZoneResolver> logger)
    {
        _coreHrEmployees = coreHrEmployees;
        _employees = employees;
        _legalEntities = legalEntities;
        _logger = logger;
    }

    public async Task<TimeZoneInfo> ResolveAsync(
        Guid tenantId,
        Guid employeeId,
        CancellationToken ct)
    {
        var employee = await _coreHrEmployees.GetByIdAsync(tenantId, employeeId, ct)
            ?? await _employees.GetByUserIdAsync(tenantId, employeeId, ct);

        if (employee?.LegalEntityId is null)
        {
            LogInvalid(tenantId, employeeId);
            return TimeZoneInfo.Utc;
        }

        var legalEntity = await _legalEntities.GetByIdForTenantAsync(
            tenantId, employee.LegalEntityId.Value, ct);

        if (legalEntity is null || string.IsNullOrWhiteSpace(legalEntity.Timezone))
        {
            LogInvalid(tenantId, employeeId);
            return TimeZoneInfo.Utc;
        }

        try
        {
            return TimeZoneInfo.FindSystemTimeZoneById(legalEntity.Timezone.Trim());
        }
        catch (TimeZoneNotFoundException)
        {
            LogInvalid(tenantId, employeeId);
            return TimeZoneInfo.Utc;
        }
        catch (InvalidTimeZoneException)
        {
            LogInvalid(tenantId, employeeId);
            return TimeZoneInfo.Utc;
        }
    }

    private void LogInvalid(Guid tenantId, Guid employeeId)
        => _logger.LogWarning(
            "invalid_monitoring_timezone TenantId={TenantId} EmployeeId={EmployeeId}",
            tenantId,
            employeeId);
}
