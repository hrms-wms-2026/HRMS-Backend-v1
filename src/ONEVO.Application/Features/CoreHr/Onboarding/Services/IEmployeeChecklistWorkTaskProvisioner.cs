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
}
