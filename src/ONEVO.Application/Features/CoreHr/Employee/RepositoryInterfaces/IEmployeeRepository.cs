using ONEVO.Application.Features.CoreHr.Employee.DTOs.Responses;
using ONEVO.Application.Features.CoreHr.Employee.Models;

namespace ONEVO.Application.Features.CoreHr.Employee.RepositoryInterfaces;

public sealed record EmployeeListFilter(string? Search, Guid? DepartmentId, Guid? LegalEntityId);

public interface IEmployeeRepository
{
    Task<(IReadOnlyList<EmployeeListItemResponse> Items, int TotalCount)> ListVisibleAsync(
        Guid tenantId,
        EmployeeVisibilityScope scope,
        EmployeeListFilter filter,
        int page,
        int pageSize,
        CancellationToken ct = default);

    Task<EmployeeListItemResponse?> GetVisibleByIdAsync(
        Guid tenantId,
        EmployeeVisibilityScope scope,
        Guid employeeId,
        CancellationToken ct = default);

    Task<ONEVO.Domain.Features.CoreHr.Entities.Employee?> GetByIdAsync(
        Guid tenantId, Guid employeeId, CancellationToken ct = default);

    Task<bool> EmailExistsAsync(Guid tenantId, string email, Guid? excludeId, CancellationToken ct = default);

    Task<bool> EmployeeNumberExistsAsync(Guid tenantId, string employeeNumber, Guid? excludeId, CancellationToken ct = default);

    Task<int> CountActiveAsync(Guid tenantId, CancellationToken ct = default);

    Task AddAsync(ONEVO.Domain.Features.CoreHr.Entities.Employee employee, CancellationToken ct = default);

    Task<int> SaveChangesAsync(CancellationToken ct = default);
}
