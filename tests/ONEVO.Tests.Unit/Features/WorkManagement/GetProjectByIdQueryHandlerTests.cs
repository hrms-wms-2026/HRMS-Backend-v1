using Moq;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.Auth.Permission.ServiceInterfaces;
using ONEVO.Application.Features.WorkManagement.Projects.Queries.GetProjectById;
using ONEVO.Application.Features.WorkManagement.Projects.RepositoryInterfaces;
using ONEVO.Application.Features.WorkManagement.ProjectMembers.RepositoryInterfaces;
using ONEVO.Domain.Features.WorkManagement.Projects.Entities;
using Xunit;

namespace ONEVO.Tests.Unit.Features.WorkManagement;

public class GetProjectByIdQueryHandlerTests
{
    private static readonly Guid TenantId = Guid.NewGuid();
    private static readonly Guid UserId = Guid.NewGuid();
    private static readonly Guid ProjectId = Guid.NewGuid();
    private static readonly Guid LeadId = Guid.NewGuid();

    private static Project Project(bool isActive = true) => new()
    {
        Id = ProjectId, TenantId = TenantId, LeadId = LeadId, IsActive = isActive,
        Name = "P", Identifier = "P1", CreatedAt = DateTimeOffset.UtcNow
    };

    private (GetProjectByIdQueryHandler Handler, Mock<IProjectMemberRepository> Members) BuildHandler(
        Project? project, List<string> permissions, bool isActiveMember)
    {
        var currentUser = new Mock<ICurrentUser>();
        currentUser.SetupGet(x => x.IsAuthenticated).Returns(true);
        currentUser.SetupGet(x => x.TenantId).Returns(TenantId);
        currentUser.SetupGet(x => x.UserId).Returns(UserId);

        var projects = new Mock<IProjectRepository>();
        projects.Setup(x => x.GetByIdForTenantAsync(TenantId, ProjectId, It.IsAny<CancellationToken>())).ReturnsAsync(project);

        var members = new Mock<IProjectMemberRepository>();
        members.Setup(x => x.HasActiveMembershipAsync(TenantId, ProjectId, UserId, It.IsAny<CancellationToken>())).ReturnsAsync(isActiveMember);

        var permissionResolver = new Mock<IPermissionResolver>();
        permissionResolver.Setup(x => x.ResolveAsync(UserId, TenantId, It.IsAny<Guid?>(), It.IsAny<CancellationToken>())).ReturnsAsync(permissions);

        var handler = new GetProjectByIdQueryHandler(currentUser.Object, projects.Object, members.Object, permissionResolver.Object);
        return (handler, members);
    }

    [Fact]
    public async Task Handle_HasReadPermission_SucceedsWithoutCheckingMembership()
    {
        var (handler, members) = BuildHandler(Project(), ["projects:read"], isActiveMember: false);

        var result = await handler.Handle(new GetProjectByIdQuery(ProjectId), CancellationToken.None);

        Assert.True(result.IsSuccess);
        members.Verify(x => x.HasActiveMembershipAsync(It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Handle_NoPermissionButActiveMember_Succeeds()
    {
        var (handler, _) = BuildHandler(Project(), [], isActiveMember: true);

        var result = await handler.Handle(new GetProjectByIdQuery(ProjectId), CancellationToken.None);

        Assert.True(result.IsSuccess);
    }

    [Fact]
    public async Task Handle_NoPermissionAndNotMember_ReturnsForbidden()
    {
        var (handler, _) = BuildHandler(Project(), [], isActiveMember: false);

        var result = await handler.Handle(new GetProjectByIdQuery(ProjectId), CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(403, result.StatusCode);
    }

    [Fact]
    public async Task Handle_WildcardPermission_Succeeds()
    {
        var (handler, _) = BuildHandler(Project(), ["*"], isActiveMember: false);

        var result = await handler.Handle(new GetProjectByIdQuery(ProjectId), CancellationToken.None);

        Assert.True(result.IsSuccess);
    }

    [Fact]
    public async Task Handle_InactiveProject_ReturnsNotFound()
    {
        var (handler, _) = BuildHandler(Project(isActive: false), ["projects:read"], isActiveMember: false);

        var result = await handler.Handle(new GetProjectByIdQuery(ProjectId), CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(404, result.StatusCode);
    }

    [Fact]
    public async Task Handle_LeadCaller_IsLeadTrue()
    {
        var currentUser = new Mock<ICurrentUser>();
        currentUser.SetupGet(x => x.IsAuthenticated).Returns(true);
        currentUser.SetupGet(x => x.TenantId).Returns(TenantId);
        currentUser.SetupGet(x => x.UserId).Returns(LeadId);

        var projects = new Mock<IProjectRepository>();
        projects.Setup(x => x.GetByIdForTenantAsync(TenantId, ProjectId, It.IsAny<CancellationToken>())).ReturnsAsync(Project());

        var members = new Mock<IProjectMemberRepository>();
        var permissionResolver = new Mock<IPermissionResolver>();
        permissionResolver.Setup(x => x.ResolveAsync(LeadId, TenantId, It.IsAny<Guid?>(), It.IsAny<CancellationToken>())).ReturnsAsync(["projects:read"]);

        var handler = new GetProjectByIdQueryHandler(currentUser.Object, projects.Object, members.Object, permissionResolver.Object);

        var result = await handler.Handle(new GetProjectByIdQuery(ProjectId), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.True(result.Value!.IsLead);
    }
}
