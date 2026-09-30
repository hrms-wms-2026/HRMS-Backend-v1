using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using ONEVO.Application.Features.WorkManagement.Leadership.Services;
using ONEVO.Application.Features.WorkManagement.Objectives.RepositoryInterfaces;
using ONEVO.Application.Features.WorkManagement.ProjectMembers.RepositoryInterfaces;
using Xunit;

namespace ONEVO.Tests.Unit.Features.WorkManagement.Leadership;

public sealed class WorkLeadershipServiceTests
{
    private static readonly Guid Tenant = Guid.NewGuid(), Me = Guid.NewGuid(), Le = Guid.NewGuid(), Project = Guid.NewGuid();

    [Fact]
    public async Task No_owned_modules_short_circuits_without_loading_trees()
    {
        var objectives = new Mock<IObjectiveRepository>();
        objectives.Setup(x => x.ListActiveOwnedIdsAsync(Tenant, Me, Le, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Array.Empty<(Guid, Guid)>());
        var members = new Mock<IProjectMemberRepository>(MockBehavior.Strict);
        var sut = new WorkLeadershipService(objectives.Object, members.Object, NullLogger<WorkLeadershipService>.Instance);

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
        var sut = new WorkLeadershipService(objectives.Object, members.Object, NullLogger<WorkLeadershipService>.Instance);

        var scope = await sut.ResolveLedScopeAsync(Tenant, Me, Le);

        Assert.Equal(module, Assert.Single(scope.HeadModules).ObjectiveId);
        objectives.Verify(x => x.ListActiveTreeForProjectsAsync(Tenant, It.IsAny<IReadOnlyCollection<Guid>>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task LeadsAnyWorkAsync_delegates_to_the_repository_probe()
    {
        var objectives = new Mock<IObjectiveRepository>();
        objectives.Setup(x => x.AnyActiveOwnedAsync(Tenant, Me, Le, It.IsAny<CancellationToken>())).ReturnsAsync(true);
        var members = new Mock<IProjectMemberRepository>(MockBehavior.Strict);
        var sut = new WorkLeadershipService(objectives.Object, members.Object, NullLogger<WorkLeadershipService>.Instance);

        Assert.True(await sut.LeadsAnyWorkAsync(Tenant, Me, Le));
    }
}
