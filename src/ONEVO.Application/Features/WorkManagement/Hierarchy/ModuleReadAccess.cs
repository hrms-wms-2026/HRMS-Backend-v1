using ONEVO.Application.Features.WorkManagement.ProjectMembers.RepositoryInterfaces;
using ONEVO.Domain.Features.WorkManagement.Objectives.Entities;

namespace ONEVO.Application.Features.WorkManagement.Hierarchy;

/// <summary>"Can this employee read this Module?" - true when they are an active member of the Module
/// or of any of its ancestors, since parent membership cascades down. The shared check behind the
/// per-Module read queries (detail, members, subtree, sprints, tasks).</summary>
public interface IModuleReadAccess
{
    Task<bool> CanReadAsync(Guid tenantId, Objective module, Guid employeeId, CancellationToken ct = default);
}

public sealed class ModuleReadAccess : IModuleReadAccess
{
    private readonly IWorkHierarchyService _hierarchy;
    private readonly IProjectMemberRepository _members;

    public ModuleReadAccess(IWorkHierarchyService hierarchy, IProjectMemberRepository members)
    {
        _hierarchy = hierarchy;
        _members = members;
    }

    public async Task<bool> CanReadAsync(Guid tenantId, Objective module, Guid employeeId, CancellationToken ct = default)
    {
        var tree = await _hierarchy.LoadTreeAsync(tenantId, module.ProjectId, ct);
        var selfAndAncestorIds = tree.AncestorChain(module.Id).Select(m => m.Id).ToList();
        if (!selfAndAncestorIds.Contains(module.Id))
            selfAndAncestorIds.Insert(0, module.Id);

        return await _members.HasActiveMembershipForAnyObjectiveAsync(tenantId, module.ProjectId, employeeId, selfAndAncestorIds, ct);
    }
}
