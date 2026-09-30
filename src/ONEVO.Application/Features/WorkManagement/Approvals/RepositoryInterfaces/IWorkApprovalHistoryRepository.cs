namespace ONEVO.Application.Features.WorkManagement.Approvals.RepositoryInterfaces;

public sealed record WorkApprovalHistoryRecord(
    Guid Id,
    Guid ObjectiveId,
    string Kind,
    string Status,
    string SubjectTitle,
    string? PayloadJson,
    Guid RequestedById,
    Guid ApproverId,
    Guid? DecidedById,
    string? DecisionComment,
    DateTimeOffset CreatedAt,
    DateTimeOffset? DecidedAt);

public interface IWorkApprovalHistoryRepository
{
    Task<IReadOnlyList<WorkApprovalHistoryRecord>> ListForEmployeeAsync(
        Guid tenantId,
        Guid projectId,
        Guid employeeId,
        CancellationToken ct = default);

    /// <summary>Cross-project: requests this employee made (task creation/edit, objective changes,
    /// task status changes) plus project invitations sent to them, created in
    /// fromUtc &lt;= CreatedAt &lt; toUtcExclusive, newest first. For the employee Overview.</summary>
    Task<IReadOnlyList<WorkApprovalHistoryRecord>> ListRequestedByEmployeeAsync(
        Guid tenantId,
        Guid employeeId,
        DateTimeOffset fromUtc,
        DateTimeOffset toUtcExclusive,
        CancellationToken ct = default);
}
