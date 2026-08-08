using Microsoft.EntityFrameworkCore;
using ONEVO.Application.Features.WorkManagement.ProjectMembers.RepositoryInterfaces;
using ONEVO.Domain.Features.WorkManagement.ProjectMembers.Entities;

namespace ONEVO.Infrastructure.Persistence.Repositories.WorkManagement;

public class EfProjectMemberRepository : IProjectMemberRepository
{
    private readonly ApplicationDbContext _db;

    public EfProjectMemberRepository(ApplicationDbContext db) => _db = db;

    public async Task AddAsync(ProjectMember member, CancellationToken ct = default)
    {
        await _db.ProjectMembers.AddAsync(member, ct);
    }

    public async Task<bool> HasActiveMembershipAsync(Guid tenantId, Guid projectId, Guid userId, CancellationToken ct = default)
    {
        return await _db.ProjectMembers
            .AsNoTracking()
            .AnyAsync(m => m.TenantId == tenantId && m.ProjectId == projectId && m.UserId == userId && m.IsActive, ct);
    }

    public async Task<ProjectMember?> GetTrackedForObjectiveAsync(Guid tenantId, Guid projectId, Guid objectiveId, Guid userId, CancellationToken ct = default)
    {
        return await _db.ProjectMembers
            .FirstOrDefaultAsync(m => m.TenantId == tenantId && m.ProjectId == projectId && m.ObjectiveId == objectiveId && m.UserId == userId, ct);
    }

    public async Task<bool> HasActiveMembershipExcludingObjectiveAsync(Guid tenantId, Guid projectId, Guid userId, Guid excludingObjectiveId, CancellationToken ct = default)
    {
        return await _db.ProjectMembers
            .AsNoTracking()
            .AnyAsync(m => m.TenantId == tenantId && m.ProjectId == projectId && m.UserId == userId
                        && m.ObjectiveId != excludingObjectiveId && m.IsActive, ct);
    }

    public async Task<bool> HasActiveMembershipForAnyObjectiveAsync(Guid tenantId, Guid projectId, Guid userId, IReadOnlyList<Guid> objectiveIds, CancellationToken ct = default)
    {
        return await _db.ProjectMembers
            .AsNoTracking()
            .AnyAsync(m => m.TenantId == tenantId && m.ProjectId == projectId && m.UserId == userId
                        && m.IsActive && objectiveIds.Contains(m.ObjectiveId), ct);
    }

    public async Task<IReadOnlyList<Guid>> GetActiveObjectiveIdsForUserInProjectAsync(Guid tenantId, Guid projectId, Guid userId, CancellationToken ct = default)
    {
        return await _db.ProjectMembers
            .AsNoTracking()
            .Where(m => m.TenantId == tenantId && m.ProjectId == projectId && m.UserId == userId && m.IsActive)
            .Select(m => m.ObjectiveId)
            .ToListAsync(ct);
    }

    public async Task<IReadOnlyList<ProjectMember>> ListInactiveMembershipsForUserAsync(Guid tenantId, Guid userId, CancellationToken ct = default)
    {
        return await _db.ProjectMembers
            .AsNoTracking()
            .Where(m => m.TenantId == tenantId && m.UserId == userId && !m.IsActive && m.RemovedAt != null)
            .OrderByDescending(m => m.RemovedAt)
            .ToListAsync(ct);
    }

    public async Task<IReadOnlyList<ProjectMember>> ListForUserInProjectAsync(Guid tenantId, Guid projectId, Guid userId, CancellationToken ct = default)
    {
        return await _db.ProjectMembers
            .AsNoTracking()
            .Where(m => m.TenantId == tenantId && m.ProjectId == projectId && m.UserId == userId)
            .ToListAsync(ct);
    }

    public void Update(ProjectMember member)
    {
        _db.ProjectMembers.Update(member);
    }
}
