using Microsoft.EntityFrameworkCore;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.Users.RepositoryInterfaces;
using ONEVO.Domain.Features.AgentGateway.Entities;
using ONEVO.Domain.Features.Configuration.Entities;
using ONEVO.Domain.Features.CoreHr.Entities;
using ONEVO.Domain.Features.IdentityVerification.Entities;
using ONEVO.Infrastructure.Persistence;

namespace ONEVO.Infrastructure.Persistence.Repositories.Users;

public sealed class EfUserProfileRepository : IUserProfileRepository
{
    private readonly ApplicationDbContext _db;
    private readonly ITenantContext _tenantContext;

    public EfUserProfileRepository(ApplicationDbContext db, ITenantContext tenantContext)
    {
        _db = db;
        _tenantContext = tenantContext;
    }

    public async Task<Employee?> GetEmployeeByUserIdAsync(Guid userId, CancellationToken ct)
        => await _db.Employees
            .Where(e => e.TenantId == _tenantContext.TenantId && e.UserId == userId && !e.IsDeleted)
            .FirstOrDefaultAsync(ct);

    public async Task<IReadOnlyList<RegisteredAgent>> GetAgentsByEmployeeIdAsync(Guid employeeId, CancellationToken ct)
        => await _db.RegisteredAgents
            .Where(a => a.TenantId == _tenantContext.TenantId && a.EmployeeId == employeeId && a.Status != "revoked")
            .OrderByDescending(a => a.LastHeartbeatAt)
            .ToListAsync(ct);

    public async Task<EmployeeWorkLocationSettings?> GetWorkLocationSettingsAsync(Guid employeeId, CancellationToken ct)
        => await _db.EmployeeWorkLocationSettings
            .Where(s => s.TenantId == _tenantContext.TenantId && s.EmployeeId == employeeId)
            .FirstOrDefaultAsync(ct);

    public async Task<VerificationReferencePhoto?> GetActiveReferencePhotoAsync(Guid employeeId, CancellationToken ct)
        => await _db.VerificationReferencePhotos
            .Where(v => v.TenantId == _tenantContext.TenantId && v.EmployeeId == employeeId && v.IsActive)
            .FirstOrDefaultAsync(ct);
}
