using ONEVO.Application.Common.Models;
using ONEVO.Application.Common.RepositoryInterfaces;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.WorkManagement.Common.OutboxHandlers;
using ONEVO.Application.Features.WorkManagement.Common.Services;
using ONEVO.Application.Features.WorkManagement.Hierarchy;
using ONEVO.Application.Features.WorkManagement.Objectives.RepositoryInterfaces;
using ONEVO.Application.Features.WorkManagement.ProjectInvitations.RepositoryInterfaces;
using ONEVO.Application.Features.WorkManagement.ProjectMembers.RepositoryInterfaces;
using ONEVO.Domain.Features.CoreHr.Entities;
using ONEVO.Domain.Features.WorkManagement.Objectives.Entities;
using ONEVO.Domain.Features.WorkManagement.ProjectInvitations.Entities;
using ONEVO.Domain.Features.WorkManagement.ProjectMembers.Entities;
using ONEVO.Domain.Lookups;

namespace ONEVO.Application.Features.WorkManagement.Objectives.Services;

public class MilestoneMembershipCoordinator : IMilestoneMembershipCoordinator
{
    private readonly IEmployeeRepository _employees;
    private readonly IProjectMemberRepository _members;
    private readonly IObjectiveRepository _objectives;
    private readonly IProjectMemberInvitationRepository _invitations;
    private readonly ICallerIdentityResolver _identity;
    private readonly IOutboxWriter _outboxWriter;

    public MilestoneMembershipCoordinator(
        IEmployeeRepository employees, IProjectMemberRepository members, IObjectiveRepository objectives,
        IProjectMemberInvitationRepository invitations, ICallerIdentityResolver identity, IOutboxWriter outboxWriter)
    {
        _employees = employees;
        _members = members;
        _objectives = objectives;
        _invitations = invitations;
        _identity = identity;
        _outboxWriter = outboxWriter;
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

    public async Task<Result<ProjectMemberInvitation>> ApplyMemberAddAsync(
        Guid tenantId, Objective module, Guid requestedByEmployeeId, Guid employeeId, CancellationToken ct = default)
    {
        var assignee = await GetActiveAssigneeAsync(tenantId, employeeId, ct);
        if (assignee is null)
            return Result<ProjectMemberInvitation>.Failure("The member must be an active employee in this tenant.");

        if (await HasActiveMembershipAsync(tenantId, module.ProjectId, module.Id, assignee.Id, ct))
            return Result<ProjectMemberInvitation>.Conflict("This employee is already a member.");

        if (await _invitations.GetPendingForObjectiveAndEmployeeAsync(tenantId, module.Id, assignee.Id, ct) is not null)
            return Result<ProjectMemberInvitation>.Conflict("An invitation is already pending for this employee on this milestone.");

        var invitation = new ProjectMemberInvitation
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            ProjectId = module.ProjectId,
            ObjectiveId = module.Id,
            InvitedEmployeeId = assignee.Id,
            InviteType = ProjectInvitationTypes.Member,
            Status = ProjectInvitationStatuses.Pending,
            InvitedById = requestedByEmployeeId
        };
        await _invitations.AddAsync(invitation, ct);

        var names = await _identity.ResolveDisplayNamesByEmployeeIdAsync(tenantId, [requestedByEmployeeId], ct);
        var inviterDisplayName = names.GetValueOrDefault(requestedByEmployeeId) ?? "A teammate";
        await _outboxWriter.EnqueueAsync(
            OutboxMessageTypes.WorkNotification,
            new WorkNotificationPayload(
                tenantId,
                assignee.UserId,
                "work_objective_invitation_created",
                new Dictionary<string, string>
                {
                    ["inviterName"] = inviterDisplayName,
                    ["objectiveName"] = module.Title,
                    ["inviteType"] = ProjectInvitationTypes.Member
                },
                "project_member_invitation",
                invitation.Id),
            tenantId,
            ct);

        return Result<ProjectMemberInvitation>.Success(invitation);
    }

    public async Task<Result> ApplyMemberRemoveAsync(Guid tenantId, Objective module, Guid employeeId, CancellationToken ct = default)
    {
        if (await HasActiveMembershipAsync(tenantId, module.ProjectId, module.Id, employeeId, ct))
        {
            await DeactivateMembershipAsync(tenantId, module.ProjectId, module.Id, employeeId, ct);
            return Result.Success();
        }

        var pendingInvite = await _invitations.GetTrackedPendingForObjectiveAndEmployeeAsync(tenantId, module.Id, employeeId, ct);
        if (pendingInvite is null)
            return Result.NotFound("This employee has no active membership or pending invitation on this milestone.");

        pendingInvite.Status = ProjectInvitationStatuses.Cancelled;
        pendingInvite.DecidedAt = DateTimeOffset.UtcNow;
        _invitations.Update(pendingInvite);
        return Result.Success();
    }
}
