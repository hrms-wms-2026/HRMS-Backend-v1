using Microsoft.EntityFrameworkCore;
using ONEVO.Application.Features.WorkManagement.ProjectInvitations.RepositoryInterfaces;
using ONEVO.Domain.Features.WorkManagement.ProjectInvitations.Entities;

namespace ONEVO.Infrastructure.Persistence.Repositories.WorkManagement;

public class EfProjectMemberInvitationRepository : IProjectMemberInvitationRepository
{
    private readonly ApplicationDbContext _db;

    public EfProjectMemberInvitationRepository(ApplicationDbContext db) => _db = db;

    public async Task AddAsync(ProjectMemberInvitation invitation, CancellationToken ct = default)
    {
        await _db.ProjectMemberInvitations.AddAsync(invitation, ct);
    }

    public async Task<ProjectMemberInvitation?> GetByIdForTenantAsync(Guid tenantId, Guid id, CancellationToken ct = default)
    {
        return await _db.ProjectMemberInvitations
            .AsNoTracking()
            .FirstOrDefaultAsync(i => i.TenantId == tenantId && i.Id == id, ct);
    }

    public async Task<ProjectMemberInvitation?> GetTrackedByIdForTenantAsync(Guid tenantId, Guid id, CancellationToken ct = default)
    {
        return await _db.ProjectMemberInvitations
            .FirstOrDefaultAsync(i => i.TenantId == tenantId && i.Id == id, ct);
    }

    public async Task<ProjectMemberInvitation?> GetPendingForObjectiveAndEmployeeAsync(Guid tenantId, Guid objectiveId, Guid employeeId, CancellationToken ct = default)
    {
        return await _db.ProjectMemberInvitations
            .AsNoTracking()
            .FirstOrDefaultAsync(i => i.TenantId == tenantId && i.ObjectiveId == objectiveId
                && i.InvitedEmployeeId == employeeId && i.Status == ProjectInvitationStatuses.Pending, ct);
    }

    public async Task<ProjectMemberInvitation?> GetTrackedPendingForObjectiveAndEmployeeAsync(Guid tenantId, Guid objectiveId, Guid employeeId, CancellationToken ct = default)
    {
        return await _db.ProjectMemberInvitations
            .FirstOrDefaultAsync(i => i.TenantId == tenantId && i.ObjectiveId == objectiveId
                && i.InvitedEmployeeId == employeeId && i.Status == ProjectInvitationStatuses.Pending, ct);
    }

    public async Task<IReadOnlyList<ProjectMemberInvitation>> ListPendingForObjectiveAsync(Guid tenantId, Guid objectiveId, CancellationToken ct = default)
    {
        return await _db.ProjectMemberInvitations
            .AsNoTracking()
            .Where(i => i.TenantId == tenantId && i.ObjectiveId == objectiveId && i.Status == ProjectInvitationStatuses.Pending)
            .ToListAsync(ct);
    }

    public async Task<IReadOnlyList<ProjectMemberInvitation>> ListPendingForEmployeeAsync(Guid tenantId, Guid employeeId, CancellationToken ct = default)
    {
        return await _db.ProjectMemberInvitations
            .AsNoTracking()
            .Where(i => i.TenantId == tenantId && i.InvitedEmployeeId == employeeId && i.Status == ProjectInvitationStatuses.Pending)
            .ToListAsync(ct);
    }

    public void Update(ProjectMemberInvitation invitation)
    {
        _db.ProjectMemberInvitations.Update(invitation);
    }
}
