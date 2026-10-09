using Moq;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.Dashboard.Team.DTOs;
using ONEVO.Application.Features.Dashboard.Team.Sources;
using ONEVO.Application.Features.WorkManagement.Approvals.Services;
using ONEVO.Application.Features.WorkManagement.Common.Services;
using ONEVO.Application.Features.WorkManagement.Leadership.Services;
using ONEVO.Domain.Features.WorkManagement.Approvals.Entities;
using Xunit;

namespace ONEVO.Tests.Unit.Features.Dashboard.Team.Sources;

public sealed class WorkObjectiveChangeTeamActionSourceTests
{
    private static readonly Guid TenantId = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
    private static readonly Guid UserId = Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb");
    private static readonly Guid CallerEmployeeId = Guid.Parse("cccccccc-cccc-cccc-cccc-cccccccccccc");
    private static readonly Guid LegalEntityId = Guid.Parse("dddddddd-dddd-dddd-dddd-dddddddddddd");

    private static WorkApprovalRequest Request(
        Guid objectiveId, Guid requestedById, string actionType, string title, DateTimeOffset createdAt) => new()
    {
        Id = Guid.NewGuid(), TenantId = TenantId, ProjectId = Guid.NewGuid(), ActionType = actionType,
        TargetType = WorkTargetTypes.Module, TargetId = objectiveId, TargetTitle = title,
        ApproverSource = WorkApprovalSources.Hierarchy, ApproverEmployeeId = CallerEmployeeId,
        RequestedByEmployeeId = requestedById, Status = WorkApprovalRequestStatuses.Pending, CreatedAt = createdAt,
    };

    [Fact]
    public async Task No_caller_employee_returns_empty_ok_summary()
    {
        var currentUser = CurrentUser();
        var identity = new Mock<ICallerIdentityResolver>();
        identity.Setup(x => x.ResolveCallerEmployeeIdAsync(TenantId, UserId, It.IsAny<CancellationToken>())).ReturnsAsync((Guid?)null);
        var source = new WorkObjectiveChangeTeamActionSource(
            currentUser, Mock.Of<IModuleEntitlementService>(), identity.Object, Mock.Of<IWorkApprovalEligibility>(MockBehavior.Strict));

        var summary = await source.GetSummaryAsync(LegalEntityId, 5, CancellationToken.None);

        Assert.Equal(0, summary.PendingCount);
    }

    [Fact]
    public async Task Queries_eligibility_filtered_to_the_six_module_action_types()
    {
        var currentUser = CurrentUser();
        var identity = new Mock<ICallerIdentityResolver>();
        identity.Setup(x => x.ResolveCallerEmployeeIdAsync(TenantId, UserId, It.IsAny<CancellationToken>())).ReturnsAsync(CallerEmployeeId);
        var eligibility = new Mock<IWorkApprovalEligibility>();
        eligibility.Setup(x => x.ListDecidableAcrossLedProjectsAsync(
                TenantId, CallerEmployeeId, LegalEntityId,
                It.Is<IReadOnlySet<string>>(s => s.SetEquals(MyTeamApprovalActionTypes.ObjectiveChange)),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(Array.Empty<WorkApprovalRequest>());

        var source = new WorkObjectiveChangeTeamActionSource(currentUser, Mock.Of<IModuleEntitlementService>(), identity.Object, eligibility.Object);
        await source.GetSummaryAsync(LegalEntityId, 5, CancellationToken.None);

        eligibility.VerifyAll();
    }

    [Fact]
    public async Task Includes_allocation_extension_alongside_the_other_request_types_and_orders_oldest_first()
    {
        var currentUser = CurrentUser();
        var identity = new Mock<ICallerIdentityResolver>();
        identity.Setup(x => x.ResolveCallerEmployeeIdAsync(TenantId, UserId, It.IsAny<CancellationToken>())).ReturnsAsync(CallerEmployeeId);
        var requesterId = Guid.NewGuid();
        var objectiveId = Guid.NewGuid();

        var editReq = Request(objectiveId, requesterId, WorkActionTypes.ModuleEdit, "Q4 Launch", DateTimeOffset.Parse("2026-09-01T00:00:00Z"));
        var extendReq = Request(objectiveId, requesterId, WorkActionTypes.ModuleAllocationExtend, "Q4 Launch", DateTimeOffset.Parse("2026-08-01T00:00:00Z"));

        var eligibility = new Mock<IWorkApprovalEligibility>();
        eligibility.Setup(x => x.ListDecidableAcrossLedProjectsAsync(TenantId, CallerEmployeeId, LegalEntityId, It.IsAny<IReadOnlySet<string>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([editReq, extendReq]);

        identity.Setup(x => x.ResolveDisplayNamesByEmployeeIdAsync(TenantId, It.IsAny<IReadOnlyList<Guid>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Dictionary<Guid, string> { [requesterId] = "Arjun M" });

        var source = new WorkObjectiveChangeTeamActionSource(
            currentUser, Mock.Of<IModuleEntitlementService>(), identity.Object, eligibility.Object);
        var summary = await source.GetSummaryAsync(LegalEntityId, 5, CancellationToken.None);

        Assert.Equal(2, summary.PendingCount);
        Assert.Equal(extendReq.CreatedAt, summary.OldestPendingAt);
        Assert.Equal(extendReq.Id, summary.TopItems[0].EntityId);
        Assert.Contains("Allocation extension", summary.TopItems[0].Title);
        Assert.Contains("Q4 Launch", summary.TopItems[0].Title);
        Assert.Equal("Edit request - Q4 Launch", summary.TopItems[1].Title);
        Assert.Equal(ActionItemLink.KindWorkRequest, summary.TopItems[0].Link.Kind);
        Assert.Equal("work_approval_request", summary.TopItems[0].Link.Params["relatedEntityType"]);
    }

    private static ICurrentUser CurrentUser()
    {
        var currentUser = new Mock<ICurrentUser>();
        currentUser.SetupGet(x => x.TenantId).Returns(TenantId);
        currentUser.SetupGet(x => x.UserId).Returns(UserId);
        return currentUser.Object;
    }
}
