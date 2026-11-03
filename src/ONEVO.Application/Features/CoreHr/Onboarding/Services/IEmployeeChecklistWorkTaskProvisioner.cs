using ONEVO.Application.Common.Models;
using ONEVO.Domain.Features.CoreHr.Entities;
using EmployeeEntity = ONEVO.Domain.Features.CoreHr.Entities.Employee;

namespace ONEVO.Application.Features.CoreHr.Onboarding.Services;

public interface IEmployeeChecklistWorkTaskProvisioner
{
    Task<Result<OfficeProjectContext>> EnsureOfficeProjectAsync(
        Guid tenantId,
        Guid legalEntityId,
        Guid actingUserId,
        DateOnly targetDate,
        CancellationToken ct = default);

    /// <summary>Stages Work tasks, assignments, memberships, links and notifications. The caller
    /// owns the transaction and final SaveChanges.</summary>
    Task<Result<int>> ProvisionAsync(
        OfficeProjectContext office,
        EmployeeEntity employee,
        IReadOnlyList<EmployeeChecklistTask> checklistTasks,
        Guid actingUserId,
        CancellationToken ct = default);

    /// <summary>Copies each provisioned task's checklist-template attachments onto its Work task.
    /// File copies save through the shared unit of work, so call this only AFTER the caller's own
    /// SaveChanges/transaction has committed. Best-effort: a failed copy never fails the caller.</summary>
    Task CopyTemplateAttachmentsAsync(
        Guid tenantId,
        IReadOnlyList<EmployeeChecklistTask> checklistTasks,
        Guid actingUserId,
        CancellationToken ct = default);
}
