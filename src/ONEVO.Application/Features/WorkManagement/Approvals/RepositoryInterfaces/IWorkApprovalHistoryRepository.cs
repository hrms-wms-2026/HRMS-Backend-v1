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
}
