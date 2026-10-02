using Microsoft.EntityFrameworkCore;
using ONEVO.Application.Features.WorkManagement.Approvals.RepositoryInterfaces;
using ONEVO.Domain.Features.WorkManagement.Approvals.Entities;

namespace ONEVO.Infrastructure.Persistence.Repositories.WorkManagement;

public class EfWorkApprovalCommentRepository : IWorkApprovalCommentRepository
{
    private readonly ApplicationDbContext _db;

    public EfWorkApprovalCommentRepository(ApplicationDbContext db) => _db = db;

    public async Task AddAsync(WorkApprovalComment comment, CancellationToken ct = default)
        => await _db.WorkApprovalComments.AddAsync(comment, ct);

    public async Task<WorkApprovalComment?> GetTrackedByIdForTenantAsync(Guid tenantId, Guid id, CancellationToken ct = default)
        => await _db.WorkApprovalComments.FirstOrDefaultAsync(c => c.TenantId == tenantId && c.Id == id, ct);

    public async Task<IReadOnlyList<WorkApprovalComment>> ListBySubjectAsync(Guid tenantId, string subjectType, Guid subjectId, CancellationToken ct = default)
        => await _db.WorkApprovalComments.AsNoTracking()
            .Where(c => c.TenantId == tenantId && c.SubjectType == subjectType && c.SubjectId == subjectId)
            .OrderBy(c => c.CreatedAt)
            .ToListAsync(ct);

    public async Task<IReadOnlyDictionary<Guid, int>> CountBySubjectsAsync(
        Guid tenantId, string subjectType, IReadOnlyCollection<Guid> subjectIds, CancellationToken ct = default)
    {
        if (subjectIds.Count == 0)
            return new Dictionary<Guid, int>();

        var ids = subjectIds.ToList();
        return await _db.WorkApprovalComments.AsNoTracking()
            .Where(c => c.TenantId == tenantId && c.SubjectType == subjectType && ids.Contains(c.SubjectId))
            .GroupBy(c => c.SubjectId)
            .Select(g => new { SubjectId = g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.SubjectId, x => x.Count, ct);
    }

    public void Update(WorkApprovalComment comment) => _db.WorkApprovalComments.Update(comment);
}
