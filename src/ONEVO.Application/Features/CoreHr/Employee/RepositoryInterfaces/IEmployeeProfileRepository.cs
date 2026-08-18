using ONEVO.Domain.Features.CoreHr.Entities;

namespace ONEVO.Application.Features.CoreHr.Employee.RepositoryInterfaces;

public interface IEmployeeProfileRepository
{
    Task<IReadOnlyList<EmployeeAddress>> ListAddressesAsync(Guid tenantId, Guid employeeId, CancellationToken ct = default);

    /// <summary>Deletes every existing address row for the employee and inserts the replacement
    /// set in the same unit of work - addresses are small (permanent/current), full-replace-on-save
    /// avoids diffing logic for a two-or-three-row collection.</summary>
    void ReplaceAddresses(Guid tenantId, Guid employeeId, IReadOnlyList<EmployeeAddress> replacement);

    Task<IReadOnlyList<EmployeeEmergencyContact>> ListEmergencyContactsAsync(Guid tenantId, Guid employeeId, CancellationToken ct = default);
    Task<EmployeeEmergencyContact?> GetEmergencyContactAsync(Guid tenantId, Guid employeeId, Guid contactId, CancellationToken ct = default);
    Task AddEmergencyContactAsync(EmployeeEmergencyContact contact, CancellationToken ct = default);
    void RemoveEmergencyContact(EmployeeEmergencyContact contact);

    Task<IReadOnlyList<EmployeeDependent>> ListDependentsAsync(Guid tenantId, Guid employeeId, CancellationToken ct = default);
    Task<EmployeeDependent?> GetDependentAsync(Guid tenantId, Guid employeeId, Guid dependentId, CancellationToken ct = default);
    Task AddDependentAsync(EmployeeDependent dependent, CancellationToken ct = default);
    void RemoveDependent(EmployeeDependent dependent);

    Task<EmployeeBankDetail?> GetPrimaryBankDetailAsync(Guid tenantId, Guid employeeId, CancellationToken ct = default);
    Task AddBankDetailAsync(EmployeeBankDetail bankDetail, CancellationToken ct = default);

    Task<int> SaveChangesAsync(CancellationToken ct = default);
}
