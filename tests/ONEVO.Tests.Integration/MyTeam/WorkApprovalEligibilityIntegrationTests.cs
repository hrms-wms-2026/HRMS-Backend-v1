using FluentAssertions;
using ONEVO.Application.Features.WorkManagement.Approvals.Services;
using ONEVO.Application.Features.WorkManagement.Common.Services;
using ONEVO.Application.Features.WorkManagement.Hierarchy;
using ONEVO.Application.Features.WorkManagement.Leadership.Services;
using ONEVO.Application.Features.WorkManagement.Objectives.Services;
using ONEVO.Domain.Features.WorkManagement.Approvals.Entities;
using ONEVO.Infrastructure.Identity.Time;
using ONEVO.Infrastructure.Persistence;
using ONEVO.Infrastructure.Persistence.Repositories.CoreHr;
using ONEVO.Infrastructure.Persistence.Repositories.WorkManagement;
using ONEVO.Infrastructure.Security;
using ONEVO.Infrastructure.Services.SharedPlatform.Outbox;
using Xunit;
using CommonEfEmployeeRepository = ONEVO.Infrastructure.Persistence.Repositories.EfEmployeeRepository;

namespace ONEVO.Tests.Integration.MyTeam;

/// <summary>Required tests from the backend merge plan §7/§0 - proves the eligibility port's
/// headline correctness claims against real PostgreSQL, not just mocks: a hierarchy-sourced
/// request's visibility follows the live module owner (not a stale stored field), HR-sourced
/// requests are unaffected by any hierarchy change, the capability gate and the Action Center
/// content can never disagree, and the no-leak-ProjectMember gap identified in the plan's
/// verification round is actually closed, not just documented. Requires Docker (Testcontainers
/// PostgreSQL).</summary>
public sealed class WorkApprovalEligibilityIntegrationTests
{
    private static WorkApprovalEligibility Eligibility(MyTeamDb helper, ApplicationDbContext db) => new(
        new EfWorkApprovalRequestRepository(db),
        new WorkHierarchyService(
            new EfObjectiveRepository(db),
            new MilestoneMembershipCoordinator(
                new CommonEfEmployeeRepository(db), new EfProjectMemberRepository(db), new EfObjectiveRepository(db),
                new EfProjectMemberInvitationRepository(db),
                new CallerIdentityResolver(new CommonEfEmployeeRepository(db), new ONEVO.Infrastructure.Persistence.Repositories.EfEntityAssetRepository(db)),
                new OutboxWriter(db, new NoOpEncryptionService(), new SystemDateTimeProvider()))),
        new EfObjectiveRepository(db));

    private static WorkLeadershipService Leadership(MyTeamDb helper, ApplicationDbContext db) => new(
        new EfObjectiveRepository(db),
        new EfProjectMemberRepository(db),
        new EfWorkApprovalRequestRepository(db),
        Eligibility(helper, db),
        Microsoft.Extensions.Logging.Abstractions.NullLogger<WorkLeadershipService>.Instance);

    private static WorkApprovalRequest HierarchyRequest(
        Guid tenantId, Guid projectId, Guid positionObjectiveId, Guid requestedBy, Guid? approverAtSubmitTime = null) => new()
    {
        Id = Guid.NewGuid(), TenantId = tenantId, ProjectId = projectId, ActionType = WorkActionTypes.ModuleEdit,
        TargetType = WorkTargetTypes.Module, TargetId = positionObjectiveId, TargetTitle = "Payments module",
        PositionObjectiveId = positionObjectiveId, ApproverSource = WorkApprovalSources.Hierarchy,
        // "Who was notified at submit time" - CanDecide must re-resolve against the LIVE tree and
        // never trust this stored value once it goes stale (e.g. after a transfer).
        ApproverEmployeeId = approverAtSubmitTime ?? requestedBy, RequestedByEmployeeId = requestedBy,
        Status = WorkApprovalRequestStatuses.Pending,
    };

    [Fact]
    public async Task ApproverTransfer_MovesVisibilityFromOldOwnerToNewOwner_WithoutTouchingTheRequestRow()
    {
        var helper = await MyTeamDb.CreateAsync();
        Guid projectId, moduleId, requesterId;
        Guid ownerAId, ownerBId;
        await using (var seed = helper.NewContext())
        {
            var requester = helper.AddEmployee(seed);
            var ownerA = helper.AddEmployee(seed);
            var ownerB = helper.AddEmployee(seed);
            requesterId = requester.Id;
            ownerAId = ownerA.Id;
            ownerBId = ownerB.Id;
            var (project, root) = helper.AddProject(seed, requester.Id);
            projectId = project;
            moduleId = helper.AddModule(seed, project, root, ownerA.Id);
            helper.AddMember(seed, project, moduleId, ownerA.Id);
            helper.AddMember(seed, project, moduleId, ownerB.Id);
            await seed.SaveChangesAsync();

            // ApproverEmployeeId is stamped as A (the owner at submit time) and is NEVER updated by
            // the transfer below - the test proves CanDecide ignores this stale value entirely.
            seed.WorkApprovalRequests.Add(HierarchyRequest(helper.TenantId, project, moduleId, requester.Id, approverAtSubmitTime: ownerA.Id));
            await seed.SaveChangesAsync();
        }

        // Before transfer: A owns the module, so A can decide and the gate reflects that; B cannot yet.
        await using (var beforeA = helper.NewContext())
            (await Leadership(helper, beforeA).HasPendingWorkApprovalsAsync(helper.TenantId, ownerAId, helper.LegalEntityId)).Should().BeTrue();
        await using (var beforeB = helper.NewContext())
            (await Leadership(helper, beforeB).HasPendingWorkApprovalsAsync(helper.TenantId, ownerBId, helper.LegalEntityId)).Should().BeFalse();

        // Transfer ownership on the live module - the request row itself is never touched (matches
        // TransferObjectiveHeadCommandHandler's real behavior: it only ever writes OwnerId on the
        // Objective, never on any already-created WorkApprovalRequest row).
        await using (var transfer = helper.NewContext())
        {
            var module = await transfer.Objectives.FindAsync(moduleId);
            module!.OwnerId = ownerBId;
            await transfer.SaveChangesAsync();
        }

        // After transfer: visibility has moved to B and away from A, driven entirely by the live
        // tree re-check - proving this isn't a stale stored ApproverEmployeeId.
        await using (var afterA = helper.NewContext())
            (await Leadership(helper, afterA).HasPendingWorkApprovalsAsync(helper.TenantId, ownerAId, helper.LegalEntityId)).Should().BeFalse(
                "A no longer owns the module, so CanDecide must no longer resolve true for A");
        await using (var afterB = helper.NewContext())
            (await Leadership(helper, afterB).HasPendingWorkApprovalsAsync(helper.TenantId, ownerBId, helper.LegalEntityId)).Should().BeTrue(
                "B now owns the module, so CanDecide must resolve true for B even though the stored ApproverEmployeeId still says the requester");

        await using var content = helper.NewContext();
        var decidableForB = await Eligibility(helper, content).ListDecidableAcrossLedProjectsAsync(
            helper.TenantId, ownerBId, helper.LegalEntityId, MyTeamApprovalActionTypes.ObjectiveChange);
        decidableForB.Should().ContainSingle(r => r.PositionObjectiveId == moduleId);
    }

    [Fact]
    public async Task HrSourcedApproval_IsUnaffectedByAnyHierarchyOwnershipChange()
    {
        var helper = await MyTeamDb.CreateAsync();
        Guid hrApproverId, unrelatedOwnerId, moduleId;
        await using (var seed = helper.NewContext())
        {
            var requester = helper.AddEmployee(seed);
            var hrApprover = helper.AddEmployee(seed);
            var unrelatedOwner = helper.AddEmployee(seed);
            hrApproverId = hrApprover.Id;
            unrelatedOwnerId = unrelatedOwner.Id;
            var (project, root) = helper.AddProject(seed, requester.Id);
            moduleId = helper.AddModule(seed, project, root, unrelatedOwner.Id);
            helper.AddMember(seed, project, moduleId, unrelatedOwner.Id);
            await seed.SaveChangesAsync();

            seed.WorkApprovalRequests.Add(new WorkApprovalRequest
            {
                Id = Guid.NewGuid(), TenantId = helper.TenantId, ProjectId = project, ActionType = WorkActionTypes.TaskCreate,
                TargetType = WorkTargetTypes.Task, TargetTitle = "New task", PositionObjectiveId = null,
                ApproverSource = WorkApprovalSources.Hr, ApproverEmployeeId = hrApprover.Id,
                RequestedByEmployeeId = requester.Id, Status = WorkApprovalRequestStatuses.Pending,
            });
            await seed.SaveChangesAsync();
        }

        await using (var beforeDb = helper.NewContext())
            (await Leadership(helper, beforeDb).HasPendingWorkApprovalsAsync(helper.TenantId, hrApproverId, helper.LegalEntityId)).Should().BeTrue();

        // Transfer the unrelated module's ownership - must have zero effect on the HR-sourced row.
        await using (var transfer = helper.NewContext())
        {
            var module = await transfer.Objectives.FindAsync(moduleId);
            module!.OwnerId = hrApproverId;
            await transfer.SaveChangesAsync();
        }

        await using var afterDb = helper.NewContext();
        (await Leadership(helper, afterDb).HasPendingWorkApprovalsAsync(helper.TenantId, hrApproverId, helper.LegalEntityId)).Should().BeTrue(
            "the HR-sourced row was already true for hrApprover before the transfer - an unrelated hierarchy change must not flip it");
    }

    [Fact]
    public async Task GateAndContent_NeverDisagree_ForTheSameData()
    {
        var helper = await MyTeamDb.CreateAsync();
        Guid ownerId;
        await using (var seed = helper.NewContext())
        {
            var requester = helper.AddEmployee(seed);
            var owner = helper.AddEmployee(seed);
            ownerId = owner.Id;
            var (project, root) = helper.AddProject(seed, requester.Id);
            var moduleId = helper.AddModule(seed, project, root, owner.Id);
            helper.AddMember(seed, project, moduleId, owner.Id);
            await seed.SaveChangesAsync();
            seed.WorkApprovalRequests.Add(HierarchyRequest(helper.TenantId, project, moduleId, requester.Id));
            await seed.SaveChangesAsync();
        }

        await using var gateDb = helper.NewContext();
        var gate = await Leadership(helper, gateDb).HasPendingWorkApprovalsAsync(helper.TenantId, ownerId, helper.LegalEntityId);

        await using var contentDb = helper.NewContext();
        var content = await Eligibility(helper, contentDb).ListDecidableAcrossLedProjectsAsync(
            helper.TenantId, ownerId, helper.LegalEntityId, MyTeamApprovalActionTypes.All);

        gate.Should().BeTrue();
        content.Should().NotBeEmpty();
        gate.Should().Be(content.Count > 0, "the gate is defined as exactly (HR flat check) OR (content non-empty) - they must never diverge");
    }

    [Fact]
    public async Task NoLeakGap_IsClosed_OwnerWithoutAProjectMemberRowStillSeesTheirApproval()
    {
        var helper = await MyTeamDb.CreateAsync();
        Guid ownerId;
        await using (var seed = helper.NewContext())
        {
            var requester = helper.AddEmployee(seed);
            var owner = helper.AddEmployee(seed);
            ownerId = owner.Id;
            var (project, root) = helper.AddProject(seed, requester.Id);
            var moduleId = helper.AddModule(seed, project, root, owner.Id);
            // Deliberately no AddMember for owner here - this is the exact anomalous "owns a module
            // but holds no ProjectMember row" condition the backend merge plan's verification round
            // (§0 finding 2 / §1) identified as a gap in the original design (which reused
            // ResolveLedScopeAsync's membership-intersected scope). The fix bypasses that
            // intersection entirely for approval eligibility, since CanDecide has no membership
            // requirement at all.
            await seed.SaveChangesAsync();
            seed.WorkApprovalRequests.Add(HierarchyRequest(helper.TenantId, project, moduleId, requester.Id));
            await seed.SaveChangesAsync();
        }

        await using var db = helper.NewContext();
        (await Leadership(helper, db).HasPendingWorkApprovalsAsync(helper.TenantId, ownerId, helper.LegalEntityId)).Should().BeTrue(
            "the fix must surface this project even though the owner has no ProjectMember row - CanDecide never required one");
    }
}
