using ONEVO.Domain.Features.WorkManagement.Approvals.Entities;

namespace ONEVO.Application.Features.WorkManagement.Approvals.RepositoryInterfaces;

public interface IWorkApprovalCommentRepository
{
    Task AddAsync(WorkApprovalComment comment, CancellationToken ct = default);
    Task<WorkApprovalComment?> GetTrackedByIdForTenantAsync(Guid tenantId, Guid id, CancellationToken ct = default);
    /// <summary>Oldest first.</summary>
    Task<IReadOnlyList<WorkApprovalComment>> ListBySubjectAsync(Guid tenantId, string subjectType, Guid subjectId, CancellationToken ct = default);
    /// <summary>Comment count per subject id; subjects without comments are absent.</summary>
    Task<IReadOnlyDictionary<Guid, int>> CountBySubjectsAsync(Guid tenantId, string subjectType, IReadOnlyCollection<Guid> subjectIds, CancellationToken ct = default);
    void Update(WorkApprovalComment comment);
}
