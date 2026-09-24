using Moq;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Common.RepositoryInterfaces;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.WorkManagement.Common.Services;
using ONEVO.Application.Features.WorkManagement.Objectives.RepositoryInterfaces;
using ONEVO.Application.Features.WorkManagement.Projects.Commands.AchieveProject;
using ONEVO.Application.Features.WorkManagement.Projects.RepositoryInterfaces;
using ONEVO.Domain.Features.WorkManagement.Objectives.Entities;
using ONEVO.Domain.Features.WorkManagement.Projects.Entities;
using Xunit;

namespace ONEVO.Tests.Unit.Features.WorkManagement;

public class AchieveProjectCommandHandlerTests
{
    private static readonly Guid TenantId = Guid.NewGuid();
    private static readonly Guid UserId = Guid.NewGuid();
    private static readonly Guid EmployeeId = Guid.NewGuid();
    private static readonly Guid OtherEmployeeId = Guid.NewGuid();
    private static readonly Guid ProjectId = Guid.NewGuid();
    private static readonly Guid DefaultObjectiveId = Guid.NewGuid();

    private static Project ActiveProject(Guid leadId, bool isAchieved = false) => new()
    {
        Id = ProjectId, TenantId = TenantId, LeadId = leadId, IsActive = true, IsAchieved = isAchieved,
        Name = "P", Identifier = "P1", CreatedAt = DateTimeOffset.UtcNow
    };

    private static Objective DefaultObjective() => new()
    {
        Id = DefaultObjectiveId, TenantId = TenantId, ProjectId = ProjectId, IsDefault = true, Title = "P",
        StartDate = new DateOnly(2026, 1, 1), EndDate = new DateOnly(2026, 6, 1), CreatedAt = DateTimeOffset.UtcNow
    };

    private (AchieveProjectCommandHandler Handler, Mock<IProjectRepository> Projects) BuildHandler(
        Project? project, List<Objective>? unachievedChildren = null, Guid? callerId = null)
    {
        var currentUser = new Mock<ICurrentUser>();
        currentUser.SetupGet(x => x.IsAuthenticated).Returns(true);
        currentUser.SetupGet(x => x.TenantId).Returns(TenantId);
        currentUser.SetupGet(x => x.UserId).Returns(callerId ?? UserId);

        var identity = new Mock<ICallerIdentityResolver>();
        identity.Setup(x => x.ResolveCallerEmployeeIdAsync(TenantId, callerId ?? UserId, It.IsAny<CancellationToken>())).ReturnsAsync(EmployeeId);

        var projects = new Mock<IProjectRepository>();
        projects.Setup(x => x.GetByIdForTenantAsync(TenantId, ProjectId, It.IsAny<CancellationToken>())).ReturnsAsync(project);

        var objectives = new Mock<IObjectiveRepository>();
        objectives.Setup(x => x.GetDefaultByProjectIdAsync(TenantId, ProjectId, It.IsAny<CancellationToken>())).ReturnsAsync(DefaultObjective());
        objectives.Setup(x => x.GetTrackedActiveDirectChildrenAsync(TenantId, DefaultObjectiveId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(unachievedChildren ?? new List<Objective>());

        var unitOfWork = new Mock<IUnitOfWork>();
        unitOfWork.Setup(x => x.SaveChangesAsync(It.IsAny<CancellationToken>())).ReturnsAsync(1);

        var handler = new AchieveProjectCommandHandler(currentUser.Object, identity.Object, projects.Object, objectives.Object, unitOfWork.Object);
        return (handler, projects);
    }

    [Fact]
    public async Task Handle_LeadAchieves_AppliesImmediately()
    {
        var (handler, projects) = BuildHandler(ActiveProject(leadId: EmployeeId));

        var result = await handler.Handle(new AchieveProjectCommand(ProjectId), CancellationToken.None);

        Assert.True(result.IsSuccess);
        projects.Verify(x => x.Update(It.Is<Project>(p => p.IsAchieved && p.AchievedAt != null)), Times.Once);
    }

    [Fact]
    public async Task Handle_DirectChildNotAchieved_ReturnsBadRequest()
    {
        var unachievedChild = new Objective { Id = Guid.NewGuid(), TenantId = TenantId, IsAchieved = false, IsActive = true };
        var (handler, _) = BuildHandler(ActiveProject(leadId: EmployeeId), unachievedChildren: new List<Objective> { unachievedChild });

        var result = await handler.Handle(new AchieveProjectCommand(ProjectId), CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(400, result.StatusCode);
    }

    [Fact]
    public async Task Handle_AlreadyAchieved_ReturnsConflict()
    {
        var (handler, _) = BuildHandler(ActiveProject(leadId: EmployeeId, isAchieved: true));

        var result = await handler.Handle(new AchieveProjectCommand(ProjectId), CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(409, result.StatusCode);
    }

    [Fact]
    public async Task Handle_NonLeadCaller_ReturnsForbidden()
    {
        var (handler, _) = BuildHandler(ActiveProject(leadId: OtherEmployeeId));

        var result = await handler.Handle(new AchieveProjectCommand(ProjectId), CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(403, result.StatusCode);
    }

    [Fact]
    public async Task Handle_ProjectNotFound_ReturnsNotFound()
    {
        var (handler, _) = BuildHandler(null);

        var result = await handler.Handle(new AchieveProjectCommand(ProjectId), CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(404, result.StatusCode);
    }
}
