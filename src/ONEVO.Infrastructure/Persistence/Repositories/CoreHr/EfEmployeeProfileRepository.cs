using Microsoft.EntityFrameworkCore;
using ONEVO.Application.Features.CoreHr.Employee.RepositoryInterfaces;
using ONEVO.Domain.Features.CoreHr.Entities;

namespace ONEVO.Infrastructure.Persistence.Repositories.CoreHr;

public class EfEmployeeProfileRepository : IEmployeeProfileRepository
{
    private readonly ApplicationDbContext _db;

    public EfEmployeeProfileRepository(ApplicationDbContext db) => _db = db;

    public async Task<IReadOnlyList<EmployeeAddress>> ListAddressesAsync(Guid tenantId, Guid employeeId, CancellationToken ct = default)
        => await _db.EmployeeAddresses.AsNoTracking()
            .Where(a => a.TenantId == tenantId && a.EmployeeId == employeeId)
            .ToListAsync(ct);

    public void ReplaceAddresses(Guid tenantId, Guid employeeId, IReadOnlyList<EmployeeAddress> replacement)
    {
        var existing = _db.EmployeeAddresses
            .Where(a => a.TenantId == tenantId && a.EmployeeId == employeeId);
        _db.EmployeeAddresses.RemoveRange(existing);
        _db.EmployeeAddresses.AddRange(replacement);
    }

    public async Task<IReadOnlyList<EmployeeEmergencyContact>> ListEmergencyContactsAsync(Guid tenantId, Guid employeeId, CancellationToken ct = default)
        => await _db.EmployeeEmergencyContacts.AsNoTracking()
            .Where(c => c.TenantId == tenantId && c.EmployeeId == employeeId)
            .ToListAsync(ct);

    public async Task<EmployeeEmergencyContact?> GetEmergencyContactAsync(Guid tenantId, Guid employeeId, Guid contactId, CancellationToken ct = default)
        => await _db.EmployeeEmergencyContacts
            .FirstOrDefaultAsync(c => c.TenantId == tenantId && c.EmployeeId == employeeId && c.Id == contactId, ct);

    public async Task AddEmergencyContactAsync(EmployeeEmergencyContact contact, CancellationToken ct = default)
        => await _db.EmployeeEmergencyContacts.AddAsync(contact, ct);

    public void RemoveEmergencyContact(EmployeeEmergencyContact contact)
        => _db.EmployeeEmergencyContacts.Remove(contact);

    public async Task<IReadOnlyList<EmployeeDependent>> ListDependentsAsync(Guid tenantId, Guid employeeId, CancellationToken ct = default)
        => await _db.EmployeeDependents.AsNoTracking()
            .Where(d => d.TenantId == tenantId && d.EmployeeId == employeeId)
            .ToListAsync(ct);

    public async Task<EmployeeDependent?> GetDependentAsync(Guid tenantId, Guid employeeId, Guid dependentId, CancellationToken ct = default)
        => await _db.EmployeeDependents
            .FirstOrDefaultAsync(d => d.TenantId == tenantId && d.EmployeeId == employeeId && d.Id == dependentId, ct);

    public async Task AddDependentAsync(EmployeeDependent dependent, CancellationToken ct = default)
        => await _db.EmployeeDependents.AddAsync(dependent, ct);

    public void RemoveDependent(EmployeeDependent dependent)
        => _db.EmployeeDependents.Remove(dependent);

    public async Task<EmployeeBankDetail?> GetPrimaryBankDetailAsync(Guid tenantId, Guid employeeId, CancellationToken ct = default)
        => await _db.EmployeeBankDetails
            .FirstOrDefaultAsync(b => b.TenantId == tenantId && b.EmployeeId == employeeId && b.IsPrimary, ct);

    public async Task AddBankDetailAsync(EmployeeBankDetail bankDetail, CancellationToken ct = default)
        => await _db.EmployeeBankDetails.AddAsync(bankDetail, ct);

    public Task<int> SaveChangesAsync(CancellationToken ct = default) => _db.SaveChangesAsync(ct);
}
