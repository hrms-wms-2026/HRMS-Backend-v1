using Moq;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Common.RepositoryInterfaces;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.WorkManagement.Objectives.RepositoryInterfaces;
using ONEVO.Application.Features.WorkManagement.Projects.Commands.EditProject;
using ONEVO.Application.Features.WorkManagement.Projects.RepositoryInterfaces;
using ONEVO.Domain.Features.WorkManagement.Objectives.Entities;
using ONEVO.Domain.Features.WorkManagement.Projects.Entities;
using Xunit;

namespace ONEVO.Tests.Unit.Features.WorkManagement;

public class EditProjectCommandHandlerTests
{
    private static readonly Guid TenantId = Guid.NewGuid();
    private static readonly Guid UserId = Guid.NewGuid();
    private static readonly Guid OtherUserId = Guid.NewGuid();
    private static readonly Guid ProjectId = Guid.NewGuid();
    private static readonly Guid ObjectiveId = Guid.NewGuid();
    private static readonly Guid CategoryId = Guid.NewGuid();

    private static EditProjectCommand ValidCommand(string? identifier = "WEB") => new(
        ProjectId, "Website Revamp v2", "updated desc", CategoryId,
        new DateOnly(2026, 1, 1), new DateOnly(2026, 7, 1), "#111111", 12m, identifier);

    private static Project ExistingProject(Guid? leadId = null) => new()
    {
        Id = ProjectId, TenantId = TenantId, CategoryId = CategoryId, Name = "Website Revamp",
        Identifier = "WEB", LeadId = leadId ?? UserId, StartDate = new DateOnly(2026, 1, 1),
        TargetDate = new DateOnly(2026, 6, 1), IsActive = true, CreatedAt = DateTimeOffset.UtcNow
    };

    private static Objective ExistingDefaultObjective() => new()
    {
        Id = ObjectiveId, TenantId = TenantId, ProjectId = ProjectId, IsDefault = true,
        Title = "Website Revamp", StartDate = new DateOnly(2026, 1, 1), EndDate = new DateOnly(2026, 6, 1),
        OwnerId = UserId, CreatedAt = DateTimeOffset.UtcNow
    };

    private (EditProjectCommandHandler Handler, Mock<IProjectRepository> Projects, Mock<IObjectiveRepository> Objectives) BuildHandler(
        Project? project, Objective? defaultObjective, bool categoryExists = true)
    {
        var currentUser = new Mock<ICurrentUser>();
        currentUser.SetupGet(x => x.IsAuthenticated).Returns(true);
        currentUser.SetupGet(x => x.TenantId).Returns(TenantId);
        currentUser.SetupGet(x => x.UserId).Returns(UserId);

        var projects = new Mock<IProjectRepository>();
        projects.Setup(x => x.GetTrackedByIdForTenantAsync(TenantId, ProjectId, It.IsAny<CancellationToken>())).ReturnsAsync(project);

        var objectives = new Mock<IObjectiveRepository>();
        objectives.Setup(x => x.GetTrackedDefaultByProjectIdAsync(TenantId, ProjectId, It.IsAny<CancellationToken>())).ReturnsAsync(defaultObjective);

        var categories = new Mock<IProjectCategoryRepository>();
        categories.Setup(x => x.GetByIdForTenantAsync(TenantId, CategoryId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(categoryExists ? new ProjectCategory { Id = CategoryId, TenantId = TenantId, Name = "General", IsActive = true } : null);

        var unitOfWork = new Mock<IUnitOfWork>();
        unitOfWork.Setup(x => x.SaveChangesAsync(It.IsAny<CancellationToken>())).ReturnsAsync(1);

        var handler = new EditProjectCommandHandler(currentUser.Object, projects.Object, objectives.Object, categories.Object, unitOfWork.Object);
        return (handler, projects, objectives);
    }

    [Fact]
    public async Task Handle_ValidRequest_UpdatesProjectAndCascadesDefaultObjective()
    {
        var project = ExistingProject();
        var defaultObjective = ExistingDefaultObjective();
        var (handler, projects, objectives) = BuildHandler(project, defaultObjective);

        var result = await handler.Handle(ValidCommand(), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal("Website Revamp v2", result.Value!.Name);

        // The handler fetches via the tracked-fetch methods and relies on EF's automatic change
        // detection (SaveChanges) to persist only the mutated fields - it must NOT call
        // Update() on either repository, since Update() unconditionally marks every property
        // Modified regardless of tracking state, which would silently overwrite fields this
        // handler never touched (see IProjectRepository.GetTrackedByIdForTenantAsync doc).
        projects.Verify(x => x.Update(It.IsAny<Project>()), Times.Never);
        objectives.Verify(x => x.Update(It.IsAny<Objective>()), Times.Never);

        // The same tracked instances returned by the mocked fetch were mutated in place.
        Assert.Equal("Website Revamp v2", project.Name);
        Assert.Equal(new DateOnly(2026, 7, 1), project.TargetDate);
        Assert.Equal("Website Revamp v2", defaultObjective.Title);
        Assert.Equal(new DateOnly(2026, 7, 1), defaultObjective.EndDate);
    }

    [Fact]
    public async Task Handle_ProjectNotFound_ReturnsNotFound()
    {
        var (handler, _, _) = BuildHandler(null, null);

        var result = await handler.Handle(ValidCommand(), CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(404, result.StatusCode);
    }

    [Fact]
    public async Task Handle_InactiveProject_ReturnsNotFound()
    {
        var inactiveProject = ExistingProject();
        inactiveProject.IsActive = false;
        var (handler, _, _) = BuildHandler(inactiveProject, ExistingDefaultObjective());

        var result = await handler.Handle(ValidCommand(), CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(404, result.StatusCode);
    }

    [Fact]
    public async Task Handle_IdentifierChangeAttempted_ReturnsBadRequest()
    {
        var (handler, _, _) = BuildHandler(ExistingProject(), ExistingDefaultObjective());

        var result = await handler.Handle(ValidCommand(identifier: "DIFFERENT"), CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(400, result.StatusCode);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public async Task Handle_IdentifierOmittedOrBlank_SkipsImmutabilityCheck(string? identifier)
    {
        var (handler, _, _) = BuildHandler(ExistingProject(), ExistingDefaultObjective());

        var result = await handler.Handle(ValidCommand(identifier: identifier), CancellationToken.None);

        Assert.True(result.IsSuccess);
    }

    [Fact]
    public async Task Handle_CategoryNotFoundForTenant_ReturnsNotFound()
    {
        var (handler, _, _) = BuildHandler(ExistingProject(), ExistingDefaultObjective(), categoryExists: false);

        var result = await handler.Handle(ValidCommand(), CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(404, result.StatusCode);
    }

    [Fact]
    public async Task Handle_NonLeadCaller_ReturnsForbidden()
    {
        var (handler, _, _) = BuildHandler(ExistingProject(leadId: OtherUserId), ExistingDefaultObjective());

        var result = await handler.Handle(ValidCommand(), CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(403, result.StatusCode);
    }
}
