using ONEVO.Application.Features.CoreHr.Employee.Models;

namespace ONEVO.Application.Features.CoreHr.Employee.ServiceInterfaces;

public interface IEmployeeVisibilityScopeResolver
{
    Task<EmployeeVisibilityScope> ResolveAsync(Guid tenantId, Guid userId, CancellationToken ct = default);
}
