namespace ONEVO.Application.Features.WorkManagement.Leadership.Services;

/// <summary>The single home of "Work I Lead" (My Team spec §8.3, §9.2). Relationship-based only:
/// never touches management coverage.</summary>
public interface IWorkLeadershipService
{
    Task<bool> LeadsAnyWorkAsync(Guid tenantId, Guid employeeId, Guid legalEntityId, CancellationToken ct = default);

    Task<LedWorkScope> ResolveLedScopeAsync(Guid tenantId, Guid employeeId, Guid legalEntityId, CancellationToken ct = default);

    /// <summary>EXISTS over the four Work routing predicates of My Team spec §8.2: task-creation
    /// requests routed to the caller as objective owner, task-edit requests likewise, objective
    /// change requests with ReportingManagerId == caller, and task-status-template-change requests
    /// in a project where TaskStatusChangeAccess.CanEditDirectly is true for the caller. Checked in
    /// that order, short-circuiting on the first true - a capability existence probe, never a full
    /// list materialization.</summary>
    Task<bool> HasPendingWorkApprovalsAsync(Guid tenantId, Guid employeeId, Guid legalEntityId, CancellationToken ct = default);
}
