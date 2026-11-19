using Moq;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.Dashboard.Team.DTOs;
using ONEVO.Application.Features.Dashboard.Team.Sources;
using ONEVO.Application.Features.WorkManagement.Common.Services;
using ONEVO.Application.Features.WorkManagement.Leadership.Services;
using ONEVO.Domain.Features.WorkManagement.Approvals.Entities;
using Xunit;

namespace ONEVO.Tests.Unit.Features.Dashboard.Team.Sources;

public sealed class WorkTaskCreationTeamActionSourceTests
{
    private static readonly Guid TenantId = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
    private static readonly Guid UserId = Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb");
    private static readonly Guid CallerEmployeeId = Guid.Parse("cccccccc-cccc-cccc-cccc-cccccccccccc");
    private static readonly Guid LegalEntityId = Guid.Parse("dddddddd-dddd-dddd-dddd-dddddddddddd");

    private static WorkApprovalRequest CreationRequest(Guid requestedBy, DateTimeOffset createdAt, string title) => new()
    {
        Id = Guid.NewGuid(), TenantId = TenantId, ProjectId = Guid.NewGuid(), ActionType = WorkActionTypes.TaskCreate,
        TargetType = WorkTargetTypes.Task, TargetTitle = title, ApproverSource = WorkApprovalSources.Hierarchy,
        ApproverEmployeeId = CallerEmployeeId, RequestedByEmployeeId = requestedBy,
        Status = WorkApprovalRequestStatuses.Pending, CreatedAt = createdAt,
    };

    [Theory]
    [InlineData(new[] { "tasks" }, true)]
    [InlineData(new[] { "payroll" }, false)]
    public async Task IsGatedAsync_reflects_active_work_modules(string[] activeModules, bool expected)
    {
        var currentUser = CurrentUser();
        var modules = new Mock<IModuleEntitlementService>();
        modules.Setup(x => x.GetActiveModuleKeysForTenantAsync(TenantId, It.IsAny<CancellationToken>())).ReturnsAsync(activeModules);
        var source = new WorkTaskCreationTeamActionSource(
            currentUser, modules.Object, Mock.Of<ICallerIdentityResolver>(), Mock.Of<IWorkApprovalEligibility>());

        Assert.Equal(expected, await source.IsGatedAsync(CancellationToken.None));
    }

    [Fact]
    public async Task No_caller_employee_returns_empty_ok_summary()
    {
        var currentUser = CurrentUser();
        var identity = new Mock<ICallerIdentityResolver>();
        identity.Setup(x => x.ResolveCallerEmployeeIdAsync(TenantId, UserId, It.IsAny<CancellationToken>())).ReturnsAsync((Guid?)null);
        var source = new WorkTaskCreationTeamActionSource(
            currentUser, Mock.Of<IModuleEntitlementService>(), identity.Object, Mock.Of<IWorkApprovalEligibility>(MockBehavior.Strict));

        var summary = await source.GetSummaryAsync(LegalEntityId, 5, CancellationToken.None);

        Assert.Equal(0, summary.PendingCount);
    }

    [Fact]
    public async Task Queries_eligibility_filtered_to_task_create_only()
    {
        var currentUser = CurrentUser();
        var identity = new Mock<ICallerIdentityResolver>();
        identity.Setup(x => x.ResolveCallerEmployeeIdAsync(TenantId, UserId, It.IsAny<CancellationToken>())).ReturnsAsync(CallerEmployeeId);
        var eligibility = new Mock<IWorkApprovalEligibility>();
        eligibility.Setup(x => x.ListDecidableAcrossLedProjectsAsync(
                TenantId, CallerEmployeeId, LegalEntityId,
                It.Is<IReadOnlySet<string>>(s => s.Count == 1 && s.Contains(WorkActionTypes.TaskCreate)),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(Array.Empty<WorkApprovalRequest>());

        var source = new WorkTaskCreationTeamActionSource(currentUser, Mock.Of<IModuleEntitlementService>(), identity.Object, eligibility.Object);
        await source.GetSummaryAsync(LegalEntityId, 5, CancellationToken.None);

        eligibility.VerifyAll();
    }

    [Fact]
    public async Task Orders_oldest_first_resolves_names_and_builds_the_work_request_link()
    {
        var currentUser = CurrentUser();
        var identity = new Mock<ICallerIdentityResolver>();
        identity.Setup(x => x.ResolveCallerEmployeeIdAsync(TenantId, UserId, It.IsAny<CancellationToken>())).ReturnsAsync(CallerEmployeeId);
        var requesterId = Guid.NewGuid();
        var older = CreationRequest(requesterId, DateTimeOffset.Parse("2026-08-01T00:00:00Z"), "Fix login bug");
        var newer = CreationRequest(requesterId, DateTimeOffset.Parse("2026-09-01T00:00:00Z"), "Add logging");
        var eligibility = new Mock<IWorkApprovalEligibility>();
        eligibility.Setup(x => x.ListDecidableAcrossLedProjectsAsync(TenantId, CallerEmployeeId, LegalEntityId, It.IsAny<IReadOnlySet<string>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([newer, older]);
        identity.Setup(x => x.ResolveDisplayNamesByEmployeeIdAsync(TenantId, It.IsAny<IReadOnlyList<Guid>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Dictionary<Guid, string> { [requesterId] = "Arjun M" });

        var source = new WorkTaskCreationTeamActionSource(
            currentUser, Mock.Of<IModuleEntitlementService>(), identity.Object, eligibility.Object);
        var summary = await source.GetSummaryAsync(LegalEntityId, 5, CancellationToken.None);

        Assert.Equal(2, summary.PendingCount);
        Assert.Equal(older.CreatedAt, summary.OldestPendingAt);
        Assert.Equal(older.Id, summary.TopItems[0].EntityId);
        Assert.Contains("Fix login bug", summary.TopItems[0].Title);
        Assert.Equal("Arjun M", summary.TopItems[0].SubjectName);
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
