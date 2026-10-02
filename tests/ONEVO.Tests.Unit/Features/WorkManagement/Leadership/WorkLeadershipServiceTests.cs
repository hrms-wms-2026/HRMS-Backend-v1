using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using ONEVO.Application.Features.WorkManagement.Approvals.RepositoryInterfaces;
using ONEVO.Application.Features.WorkManagement.Approvals.Services;
using ONEVO.Application.Features.WorkManagement.Leadership.Services;
using ONEVO.Application.Features.WorkManagement.Objectives.RepositoryInterfaces;
using ONEVO.Application.Features.WorkManagement.ProjectMembers.RepositoryInterfaces;
using ONEVO.Domain.Features.WorkManagement.Approvals.Entities;
using Xunit;

namespace ONEVO.Tests.Unit.Features.WorkManagement.Leadership;

public sealed class WorkLeadershipServiceTests
{
    private static readonly Guid Tenant = Guid.NewGuid(), Me = Guid.NewGuid(), Le = Guid.NewGuid(), Project = Guid.NewGuid();

    private static WorkLeadershipService Build(
        Mock<IObjectiveRepository> objectives,
        Mock<IProjectMemberRepository>? members = null,
        Mock<IWorkApprovalRequestRepository>? approvalRequests = null,
        Mock<IWorkApprovalEligibility>? eligibility = null)
    {
        members ??= new Mock<IProjectMemberRepository>(MockBehavior.Strict);
        approvalRequests ??= Default(new Mock<IWorkApprovalRequestRepository>(), m =>
            m.Setup(x => x.HasPendingForHrApproverAsync(It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<IReadOnlySet<string>>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(false));
        eligibility ??= Default(new Mock<IWorkApprovalEligibility>(), m =>
            m.Setup(x => x.ListDecidableAcrossLedProjectsAsync(It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<IReadOnlySet<string>>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(Array.Empty<WorkApprovalRequest>()));

        return new WorkLeadershipService(
            objectives.Object, members.Object, approvalRequests.Object, eligibility.Object,
            NullLogger<WorkLeadershipService>.Instance);
    }

    private static Mock<T> Default<T>(Mock<T> mock, Action<Mock<T>> setup) where T : class
    {
        setup(mock);
        return mock;
    }

    [Fact]
    public async Task No_owned_modules_short_circuits_without_loading_trees()
    {
        var objectives = new Mock<IObjectiveRepository>();
        objectives.Setup(x => x.ListActiveOwnedIdsAsync(Tenant, Me, Le, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Array.Empty<(Guid, Guid)>());
        var sut = Build(objectives);

        var scope = await sut.ResolveLedScopeAsync(Tenant, Me, Le);

        Assert.Same(LedWorkScope.Empty, scope);
        objectives.Verify(x => x.ListActiveTreeForProjectsAsync(It.IsAny<Guid>(), It.IsAny<IReadOnlyCollection<Guid>>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Builds_the_scope_from_one_tree_read_and_one_membership_read()
    {
        Guid root = Guid.NewGuid(), module = Guid.NewGuid();
        var objectives = new Mock<IObjectiveRepository>();
        objectives.Setup(x => x.ListActiveOwnedIdsAsync(Tenant, Me, Le, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new[] { (module, Project) });
        objectives.Setup(x => x.ListActiveTreeForProjectsAsync(Tenant, It.IsAny<IReadOnlyCollection<Guid>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new[]
            {
                new LedObjectiveRow(root, Project, null, "Root", true, new DateOnly(2026, 12, 1)),
                new LedObjectiveRow(module, Project, root, "Payments", false, new DateOnly(2026, 11, 1)),
            });
        var members = new Mock<IProjectMemberRepository>();
        members.Setup(x => x.ListActiveMembershipObjectiveIdsAsync(Tenant, Me, It.IsAny<IReadOnlyCollection<Guid>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new[] { module });
        var sut = Build(objectives, members);

        var scope = await sut.ResolveLedScopeAsync(Tenant, Me, Le);

        Assert.Equal(module, Assert.Single(scope.HeadModules).ObjectiveId);
        objectives.Verify(x => x.ListActiveTreeForProjectsAsync(Tenant, It.IsAny<IReadOnlyCollection<Guid>>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task LeadsAnyWorkAsync_delegates_to_the_repository_probe()
    {
        var objectives = new Mock<IObjectiveRepository>();
        objectives.Setup(x => x.AnyActiveOwnedAsync(Tenant, Me, Le, It.IsAny<CancellationToken>())).ReturnsAsync(true);
        var sut = Build(objectives);

        Assert.True(await sut.LeadsAnyWorkAsync(Tenant, Me, Le));
    }

    [Fact]
    public async Task HasPendingWorkApprovalsAsync_false_when_hr_check_false_and_eligibility_empty()
    {
        var sut = Build(new Mock<IObjectiveRepository>(MockBehavior.Strict));

        Assert.False(await sut.HasPendingWorkApprovalsAsync(Tenant, Me, Le));
    }

    [Fact]
    public async Task HasPendingWorkApprovalsAsync_true_from_hr_check_short_circuits_before_eligibility_is_called()
    {
        var approvalRequests = new Mock<IWorkApprovalRequestRepository>();
        approvalRequests.Setup(x => x.HasPendingForHrApproverAsync(Tenant, Me, MyTeamApprovalActionTypes.All, It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);
        var eligibility = new Mock<IWorkApprovalEligibility>(MockBehavior.Strict);
        var sut = Build(new Mock<IObjectiveRepository>(MockBehavior.Strict), approvalRequests: approvalRequests, eligibility: eligibility);

        Assert.True(await sut.HasPendingWorkApprovalsAsync(Tenant, Me, Le));
        eligibility.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task HasPendingWorkApprovalsAsync_hr_check_is_filtered_to_my_team_scope_not_unfiltered()
    {
        // Guards against the gate/content mismatch found in the backend merge plan's verification
        // round: an HR-sourced request of an out-of-scope ActionType (e.g. Sprint.*) must never
        // flip this gate to true, since no Action Source would ever show it.
        var approvalRequests = new Mock<IWorkApprovalRequestRepository>();
        approvalRequests.Setup(x => x.HasPendingForHrApproverAsync(Tenant, Me, It.Is<IReadOnlySet<string>>(s => s.SetEquals(MyTeamApprovalActionTypes.All)), It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);
        var sut = Build(new Mock<IObjectiveRepository>(MockBehavior.Strict), approvalRequests: approvalRequests);

        Assert.False(await sut.HasPendingWorkApprovalsAsync(Tenant, Me, Le));
        approvalRequests.VerifyAll();
    }

    [Fact]
    public async Task HasPendingWorkApprovalsAsync_true_when_eligibility_finds_a_decidable_request()
    {
        var eligibility = new Mock<IWorkApprovalEligibility>();
        eligibility.Setup(x => x.ListDecidableAcrossLedProjectsAsync(Tenant, Me, Le, MyTeamApprovalActionTypes.All, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<WorkApprovalRequest> { new() });
        var sut = Build(new Mock<IObjectiveRepository>(MockBehavior.Strict), eligibility: eligibility);

        Assert.True(await sut.HasPendingWorkApprovalsAsync(Tenant, Me, Le));
    }
}
