using ONEVO.Application.Features.CoreHr.Employee.RepositoryInterfaces;
using ONEVO.Application.Features.OrgStructure.RepositoryInterfaces;

namespace ONEVO.Application.Features.Calendar.Services;

public sealed class CalendarTimezoneResolver(IEmployeeRepository employees, ILegalEntityRepository legalEntities) : ICalendarTimezoneResolver
{
    public async Task<string> ResolveForUserAsync(Guid tenantId, Guid userId, CancellationToken ct = default)
    {
        var employee = await employees.GetDefaultForUserAsync(tenantId, userId, ct);
        if (employee is null) return "UTC";

        if (!string.IsNullOrWhiteSpace(employee.DisplayTimezone))
            return employee.DisplayTimezone;

        if (employee.LegalEntityId is Guid legalEntityId)
        {
            var legalEntity = await legalEntities.GetByIdForTenantAsync(tenantId, legalEntityId, ct);
            if (!string.IsNullOrWhiteSpace(legalEntity?.Timezone))
                return legalEntity!.Timezone!;
        }

        return "UTC";
    }
}
