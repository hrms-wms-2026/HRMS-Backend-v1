using ONEVO.Application.Common.RepositoryInterfaces;
using ONEVO.Application.Features.WorkManagement.Hierarchy;
using ONEVO.Application.Features.WorkManagement.Objectives.RepositoryInterfaces;
using ONEVO.Application.Features.WorkManagement.ProjectMembers.RepositoryInterfaces;
using ONEVO.Domain.Features.CoreHr.Entities;
using ONEVO.Domain.Features.WorkManagement.Objectives.Entities;
using ONEVO.Domain.Features.WorkManagement.ProjectMembers.Entities;
using ONEVO.Domain.Lookups;

namespace ONEVO.Application.Features.WorkManagement.Objectives.Services;

public class MilestoneMembershipCoordinator : IMilestoneMembershipCoordinator
{
    private readonly IEmployeeRepository _employees;
    private readonly IProjectMemberRepository _members;
    private readonly IObjectiveRepository _objectives;

    public MilestoneMembershipCoordinator(IEmployeeRepository employees, IProjectMemberRepository members, IObjectiveRepository objectives)
    {
        _employees = employees;
        _members = members;
        _objectives = objectives;
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

    public async Task<bool> IsEffectiveManagerAsync(Guid tenantId, Guid objectiveId, Guid employeeId, CancellationToken ct = default)
    {
        var chain = await SelfAndAncestorsAsync(tenantId, objectiveId, ct);
        if (chain.Count == 0)
            return false;
        if (chain.Any(o => o.OwnerId == employeeId))
            return true;

        return await _members.HasActiveMembershipForAnyObjectiveAsync(
            tenantId, chain[0].ProjectId, employeeId, chain.Select(o => o.Id).ToList(), ct);
    }

    public async Task<bool> IsEffectiveOwnerAsync(Guid tenantId, Guid objectiveId, Guid employeeId, CancellationToken ct = default)
        => (await SelfAndAncestorsAsync(tenantId, objectiveId, ct)).Any(o => o.OwnerId == employeeId);

    /// <summary>Self first, root last - one query for the project tree instead of one per level.</summary>
    private async Task<IReadOnlyList<Objective>> SelfAndAncestorsAsync(Guid tenantId, Guid objectiveId, CancellationToken ct)
    {
        var start = await _objectives.GetByIdForTenantAsync(tenantId, objectiveId, ct);
        if (start is null)
            return [];

        var tree = new ProjectModuleTree(await _objectives.GetAllByProjectIdAsync(tenantId, start.ProjectId, ct));
        var chain = tree.AncestorChain(start.Id);
        return chain.Count > 0 ? chain : [start];
    }
}
