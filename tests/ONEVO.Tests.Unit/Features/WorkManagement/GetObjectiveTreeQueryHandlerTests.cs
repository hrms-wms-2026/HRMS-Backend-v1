using Moq;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.WorkManagement.Objectives.Queries.GetObjectiveTree;
using ONEVO.Application.Features.WorkManagement.Objectives.RepositoryInterfaces;
using ONEVO.Application.Features.WorkManagement.Projects.RepositoryInterfaces;
using ONEVO.Application.Features.WorkManagement.ProjectMembers.RepositoryInterfaces;
using ONEVO.Domain.Features.WorkManagement.Objectives.Entities;
using ONEVO.Domain.Features.WorkManagement.Projects.Entities;
using Xunit;

namespace ONEVO.Tests.Unit.Features.WorkManagement;

public class GetObjectiveTreeQueryHandlerTests
{
    private static readonly Guid TenantId = Guid.NewGuid();
    private static readonly Guid UserId = Guid.NewGuid();
    private static readonly Guid ProjectId = Guid.NewGuid();

    private static Project ActiveProject() => new()
    {
        Id = ProjectId, TenantId = TenantId, IsActive = true, Name = "P", Identifier = "P1", CreatedAt = DateTimeOffset.UtcNow
    };

    private (GetObjectiveTreeQueryHandler Handler, Mock<IObjectiveRepository> Objectives) BuildHandler(
        Project? project, bool isMember, IReadOnlyList<Objective>? tree = null)
    {
        var currentUser = new Mock<ICurrentUser>();
        currentUser.SetupGet(x => x.IsAuthenticated).Returns(true);
        currentUser.SetupGet(x => x.TenantId).Returns(TenantId);
        currentUser.SetupGet(x => x.UserId).Returns(UserId);

        var projects = new Mock<IProjectRepository>();
        projects.Setup(x => x.GetByIdForTenantAsync(TenantId, ProjectId, It.IsAny<CancellationToken>())).ReturnsAsync(project);

        var members = new Mock<IProjectMemberRepository>();
        members.Setup(x => x.HasActiveMembershipAsync(TenantId, ProjectId, UserId, It.IsAny<CancellationToken>())).ReturnsAsync(isMember);

        var objectives = new Mock<IObjectiveRepository>();
        objectives.Setup(x => x.GetTreeByProjectIdAsync(TenantId, ProjectId, It.IsAny<CancellationToken>())).ReturnsAsync(tree ?? []);

        var handler = new GetObjectiveTreeQueryHandler(currentUser.Object, projects.Object, members.Object, objectives.Object);
        return (handler, objectives);
    }

    [Fact]
    public async Task Handle_ActiveMember_ReturnsTree()
    {
        var (handler, _) = BuildHandler(ActiveProject(), isMember: true);

        var result = await handler.Handle(new GetObjectiveTreeQuery(ProjectId), CancellationToken.None);

        Assert.True(result.IsSuccess);
    }

    [Fact]
    public async Task Handle_NotAMember_ReturnsForbidden()
    {
        var (handler, _) = BuildHandler(ActiveProject(), isMember: false);

        var result = await handler.Handle(new GetObjectiveTreeQuery(ProjectId), CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(403, result.StatusCode);
    }

    [Fact]
    public async Task Handle_ProjectNotFound_ReturnsNotFound()
    {
        var (handler, _) = BuildHandler(null, isMember: true);

        var result = await handler.Handle(new GetObjectiveTreeQuery(ProjectId), CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(404, result.StatusCode);
    }
}
