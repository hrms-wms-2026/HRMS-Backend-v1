using FluentAssertions;
using ONEVO.Application.Features.WorkManagement.Approvals.Services;
using ONEVO.Application.Features.WorkManagement.Hierarchy;
using ONEVO.Domain.Features.WorkManagement.Approvals.Entities;
using ONEVO.Domain.Features.WorkManagement.Objectives.Entities;
using Xunit;

namespace ONEVO.Tests.Unit.Features.WorkManagement.Approvals;

public class WorkApprovalDecisionRulesTests
{
    private static readonly Guid Lead = Guid.NewGuid();
    private static readonly Guid A = Guid.NewGuid();
    private static readonly Guid X = Guid.NewGuid();
    private static readonly Guid Requester = Guid.NewGuid();
    private static readonly Guid HrManager = Guid.NewGuid();
    private readonly Guid _root = Guid.NewGuid();
    private readonly Guid _p = Guid.NewGuid();

    private ProjectModuleTree Tree(Guid pOwner) => new(new[]
    {
        new Objective { Id = _root, OwnerId = Lead, IsDefault = true },
        new Objective { Id = _p, ParentObjectiveId = _root, OwnerId = pOwner },
    });

    private WorkApprovalRequest Hierarchy(Guid approver) => new()
    {
        PositionObjectiveId = _p, ApproverSource = WorkApprovalSources.Hierarchy,
        ApproverEmployeeId = approver, RequestedByEmployeeId = Requester
    };

    [Fact]
    public void AfterTransfer_NewHolderDecides_OldHolderCannot()
    {
        var request = Hierarchy(A); // A was notified before the transfer
        var tree = Tree(X);         // P now owned by X
        WorkApprovalDecisionRules.CanDecide(tree, request, X).Should().BeTrue();
        WorkApprovalDecisionRules.CanDecide(tree, request, A).Should().BeFalse();
        WorkApprovalDecisionRules.CanDecide(tree, request, Lead).Should().BeTrue();
    }

    [Fact]
    public void RequesterCanNeverDecideOwnRequest()
        => WorkApprovalDecisionRules.CanDecide(Tree(Requester), Hierarchy(Requester), Requester).Should().BeFalse();

    [Fact]
    public void HrApproval_OnlyTheResolvedApprover()
    {
        var request = new WorkApprovalRequest
        {
            PositionObjectiveId = null, ApproverSource = WorkApprovalSources.Hr,
            ApproverEmployeeId = HrManager, RequestedByEmployeeId = Lead
        };
        WorkApprovalDecisionRules.CanDecide(Tree(A), request, HrManager).Should().BeTrue();
        WorkApprovalDecisionRules.CanDecide(Tree(A), request, A).Should().BeFalse();
    }
}
