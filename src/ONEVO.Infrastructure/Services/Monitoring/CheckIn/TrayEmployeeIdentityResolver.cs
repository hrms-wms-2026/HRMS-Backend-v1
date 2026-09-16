using Microsoft.Extensions.Logging;
using ONEVO.Application.Features.CoreHr.Employee.RepositoryInterfaces;
using ONEVO.Application.Features.Monitoring.CheckIn.ServiceInterfaces;

namespace ONEVO.Infrastructure.Services.Monitoring.CheckIn;

public class TrayEmployeeIdentityResolver : ITrayEmployeeIdentityResolver
{
    private readonly IEmployeeRepository _employees;
    private readonly ILogger<TrayEmployeeIdentityResolver> _logger;

    public TrayEmployeeIdentityResolver(
        IEmployeeRepository employees, ILogger<TrayEmployeeIdentityResolver> logger)
    {
        _employees = employees;
        _logger = logger;
    }

    public async Task<Guid> ResolveEmployeeIdAsync(
        Guid tenantId, Guid userId, Guid? legalEntityId, CancellationToken ct = default)
    {
        var employee = legalEntityId.HasValue
            ? await _employees.GetByUserAndLegalEntityAsync(tenantId, userId, legalEntityId.Value, ct)
            : await _employees.GetDefaultForUserAsync(tenantId, userId, ct);

        if (employee is not null)
            return employee.Id;

        _logger.LogWarning(
            "Tray identity resolution found no Employee for TenantId={TenantId} UserId={UserId} " +
            "LegalEntityId={LegalEntityId}; falling back to UserId as the stored EmployeeId.",
            tenantId, userId, legalEntityId);
        return userId;
    }
}
