using ONEVO.Application.Common.RepositoryInterfaces;
using ONEVO.Application.Features.WorkManagement.ProjectMembers.RepositoryInterfaces;
using ONEVO.Domain.Features.CoreHr.Entities;
using ONEVO.Domain.Features.WorkManagement.ProjectMembers.Entities;
using ONEVO.Domain.Lookups;

namespace ONEVO.Application.Features.WorkManagement.Objectives.Services;

public class MilestoneMembershipCoordinator : IMilestoneMembershipCoordinator
{
    private readonly IEmployeeRepository _employees;
    private readonly IProjectMemberRepository _members;

    public MilestoneMembershipCoordinator(IEmployeeRepository employees, IProjectMemberRepository members)
    {
        _employees = employees;
        _members = members;
    }

    public async Task<Employee?> GetActiveAssigneeAsync(Guid tenantId, Guid employeeId, CancellationToken ct = default)
    {
        var employee = await _employees.GetByIdAsync(tenantId, employeeId, ct);
        return employee is not null && employee.EmploymentStatusId == EmploymentStatusIds.Active ? employee : null;
    }

    public async Task UpsertMembershipAsync(Guid tenantId, Guid projectId, Guid objectiveId, Guid employeeId, CancellationToken ct = default)
    {
        var existing = await _members.GetTrackedForObjectiveAsync(tenantId, projectId, objectiveId, employeeId, ct);

        if (existing is null)
        {
            await _members.AddAsync(new ProjectMember
            {
                Id = Guid.NewGuid(),
                TenantId = tenantId,
                ProjectId = projectId,
                ObjectiveId = objectiveId,
                EmployeeId = employeeId,
                MembershipSource = ProjectMembershipSources.ObjectiveInvitation,
                IsActive = true,
                JoinedAt = DateTimeOffset.UtcNow,
                CreatedAt = DateTimeOffset.UtcNow
            }, ct);
            return;
        }

        if (existing.IsActive)
            return;

        existing.IsActive = true;
        existing.RemovedAt = null;
        existing.JoinedAt = DateTimeOffset.UtcNow;
        _members.Update(existing);
    }

    public async Task DeactivateMembershipAsync(Guid tenantId, Guid projectId, Guid objectiveId, Guid employeeId, CancellationToken ct = default)
    {
        var existing = await _members.GetTrackedForObjectiveAsync(tenantId, projectId, objectiveId, employeeId, ct);
        if (existing is null || !existing.IsActive)
            return;

        existing.IsActive = false;
        existing.RemovedAt = DateTimeOffset.UtcNow;
        _members.Update(existing);
    }

    public Task<bool> HasOtherActiveAccessAsync(Guid tenantId, Guid projectId, Guid employeeId, Guid excludingObjectiveId, CancellationToken ct = default)
        => _members.HasActiveMembershipExcludingObjectiveAsync(tenantId, projectId, employeeId, excludingObjectiveId, ct);

    public async Task<bool> HasActiveMembershipAsync(Guid tenantId, Guid projectId, Guid objectiveId, Guid employeeId, CancellationToken ct = default)
    {
        var existing = await _members.GetTrackedForObjectiveAsync(tenantId, projectId, objectiveId, employeeId, ct);
        return existing?.IsActive == true;
    }

    public async Task<bool> IsActiveMemberAsync(Guid tenantId, Guid objectiveId, Guid employeeId, CancellationToken ct = default)
    {
        var members = await _members.ListActiveForObjectiveAsync(tenantId, objectiveId, ct);
        return members.Any(m => m.EmployeeId == employeeId);
    }
}
