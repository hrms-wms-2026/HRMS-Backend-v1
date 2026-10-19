using CoreEmployeeRepository = ONEVO.Application.Features.CoreHr.Employee.RepositoryInterfaces.IEmployeeRepository;

namespace ONEVO.Application.Features.Calendar.Services;

/// <summary>The organizer's display name for calendar notifications ("Someone" when unknown), as the commands write it.</summary>
public static class CalendarOrganizer
{
    public static async Task<string> NameAsync(CoreEmployeeRepository? employees, Guid tenantId, Guid userId, CancellationToken ct)
    {
        var employee = employees is null ? null : await employees.GetDefaultForUserAsync(tenantId, userId, ct);
        return employee is null ? "Someone" : $"{employee.FirstName} {employee.LastName}";
    }
}
