using ONEVO.Domain.Features.TimeAttendance.Entities;

namespace ONEVO.Application.Features.TimeAttendance.RepositoryInterfaces;

public interface IEmployeeWorkLocationRepository
{
    Task<EmployeeWorkLocation?> GetByEmployeeIdAsync(Guid tenantId, Guid employeeId, CancellationToken ct = default);

    Task<IReadOnlyDictionary<Guid, EmployeeWorkLocation>> ListByEmployeeIdsAsync(
        Guid tenantId, IReadOnlyCollection<Guid> employeeIds, CancellationToken ct = default);

    Task AddAsync(EmployeeWorkLocation location, CancellationToken ct = default);

    void Update(EmployeeWorkLocation location);

    /// <summary>Removes the registered location (an undone location change that created it).</summary>
    void Remove(EmployeeWorkLocation location);

    Task<int> SaveChangesAsync(CancellationToken ct = default);
}
