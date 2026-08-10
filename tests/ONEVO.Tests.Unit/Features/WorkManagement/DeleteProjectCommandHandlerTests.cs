using Moq;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Common.RepositoryInterfaces;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.WorkManagement.Projects.Commands.DeleteProject;
using ONEVO.Application.Features.WorkManagement.Projects.RepositoryInterfaces;
using ONEVO.Domain.Features.WorkManagement.Projects.Entities;
using Xunit;

namespace ONEVO.Tests.Unit.Features.WorkManagement;

public class DeleteProjectCommandHandlerTests
{
    private static readonly Guid TenantId = Guid.NewGuid();
    private static readonly Guid UserId = Guid.NewGuid();
    private static readonly Guid OtherUserId = Guid.NewGuid();
    private static readonly Guid ProjectId = Guid.NewGuid();

    private static Project ActiveProject(Guid leadId) => new()
    {
        Id = ProjectId, TenantId = TenantId, LeadId = leadId, IsActive = true,
        Name = "P", Identifier = "P1", CreatedAt = DateTimeOffset.UtcNow
    };

    private (DeleteProjectCommandHandler Handler, Mock<IProjectRepository> Projects) BuildHandler(Project? project)
    {
        var currentUser = new Mock<ICurrentUser>();
        currentUser.SetupGet(x => x.IsAuthenticated).Returns(true);
        currentUser.SetupGet(x => x.TenantId).Returns(TenantId);
        currentUser.SetupGet(x => x.UserId).Returns(UserId);

        var projects = new Mock<IProjectRepository>();
        projects.Setup(x => x.GetByIdForTenantAsync(TenantId, ProjectId, It.IsAny<CancellationToken>())).ReturnsAsync(project);

        var unitOfWork = new Mock<IUnitOfWork>();
        unitOfWork.Setup(x => x.SaveChangesAsync(It.IsAny<CancellationToken>())).ReturnsAsync(1);

        var handler = new DeleteProjectCommandHandler(currentUser.Object, projects.Object, unitOfWork.Object);
        return (handler, projects);
    }

    [Fact]
    public async Task Handle_LeadDeletesActiveProject_Succeeds()
    {
        var (handler, projects) = BuildHandler(ActiveProject(leadId: UserId));

        var result = await handler.Handle(new DeleteProjectCommand(ProjectId), CancellationToken.None);

        Assert.True(result.IsSuccess);
        projects.Verify(x => x.Update(It.Is<Project>(p => !p.IsActive)), Times.Once);
    }

    [Fact]
    public async Task Handle_NonLeadCaller_ReturnsForbidden()
    {
        var (handler, _) = BuildHandler(ActiveProject(leadId: OtherUserId));

        var result = await handler.Handle(new DeleteProjectCommand(ProjectId), CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(403, result.StatusCode);
    }

    [Fact]
    public async Task Handle_AlreadyDeleted_ReturnsConflict()
    {
        var project = ActiveProject(leadId: UserId);
        project.IsActive = false;
        var (handler, _) = BuildHandler(project);

        var result = await handler.Handle(new DeleteProjectCommand(ProjectId), CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(409, result.StatusCode);
    }

    [Fact]
    public async Task Handle_ProjectNotFound_ReturnsNotFound()
    {
        var (handler, _) = BuildHandler(null);

        var result = await handler.Handle(new DeleteProjectCommand(ProjectId), CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(404, result.StatusCode);
    }
}
