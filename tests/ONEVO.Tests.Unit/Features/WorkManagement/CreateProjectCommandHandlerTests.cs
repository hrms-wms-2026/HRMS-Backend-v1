using Moq;
using ONEVO.Application.Common.RepositoryInterfaces;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.Auth.Login.RepositoryInterfaces;
using ONEVO.Application.Features.OrgStructure.RepositoryInterfaces;
using ONEVO.Application.Features.Storage.File.ServiceInterfaces;
using ONEVO.Application.Features.WorkManagement.Objectives.RepositoryInterfaces;
using ONEVO.Application.Features.WorkManagement.Projects.Commands.CreateProject;
using ONEVO.Application.Features.WorkManagement.Projects.DTOs.Requests;
using ONEVO.Application.Features.WorkManagement.Projects.RepositoryInterfaces;
using ONEVO.Application.Features.WorkManagement.ProjectMembers.RepositoryInterfaces;
using ONEVO.Application.Features.WorkManagement.ReleaseCalendar.RepositoryInterfaces;
using ONEVO.Application.Features.WorkManagement.Versions.RepositoryInterfaces;
using ONEVO.Application.Features.WorkManagement.Labels.RepositoryInterfaces;
using ONEVO.Domain.Features.CoreHr.Entities;
using ONEVO.Domain.Features.OrgStructure.Entities;
using ONEVO.Domain.Features.WorkManagement.Projects.Entities;
using ONEVO.Domain.Lookups;
using Xunit;

namespace ONEVO.Tests.Unit.Features.WorkManagement;

public class CreateProjectCommandHandlerTests
{
    private static readonly Guid TenantId = Guid.NewGuid();
    private static readonly Guid UserId = Guid.NewGuid();
    private static readonly Guid CategoryId = Guid.NewGuid();
    private static readonly Guid LegalEntityId = Guid.NewGuid();
    private static readonly Guid EmployeeId = Guid.NewGuid();

    private static CreateProjectCommand ValidCommand(IReadOnlyList<CreateProjectLabelInput>? labels = null) => new(
        CategoryId, "Website Revamp", "WEB", "desc",
        new DateOnly(2026, 1, 1), new DateOnly(2026, 6, 1), new DateOnly(2026, 6, 15),
        "#2563EB", 10m, 40m, labels ?? [], null, null, null);

    private (CreateProjectCommandHandler Handler, Mock<IProjectRepository> Projects) BuildHandler(
        bool categoryExists = true, bool identifierExists = false, int employmentStatusId = EmploymentStatusIds.Active)
    {
        var currentUser = new Mock<ICurrentUser>();
        currentUser.SetupGet(x => x.IsAuthenticated).Returns(true);
        currentUser.SetupGet(x => x.TenantId).Returns(TenantId);
        currentUser.SetupGet(x => x.UserId).Returns(UserId);

        var categories = new Mock<IProjectCategoryRepository>();
        categories.Setup(x => x.GetByIdForTenantAsync(TenantId, CategoryId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(categoryExists ? new ProjectCategory { Id = CategoryId, TenantId = TenantId, Name = "General" } : null);

        var projects = new Mock<IProjectRepository>();
        projects.Setup(x => x.IdentifierExistsForTenantAsync(TenantId, "WEB", It.IsAny<CancellationToken>()))
            .ReturnsAsync(identifierExists);

        var objectives = new Mock<IObjectiveRepository>();
        var members = new Mock<IProjectMemberRepository>();
        var versions = new Mock<IProjectVersionRepository>();
        var releaseCalendar = new Mock<IReleaseCalendarRepository>();
        var labels = new Mock<ILabelRepository>();
        var entityAssets = new Mock<IEntityAssetRepository>();
        var employees = new Mock<IEmployeeRepository>();
        employees.Setup(x => x.GetByUserIdAsync(TenantId, UserId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Employee { Id = EmployeeId, TenantId = TenantId, UserId = UserId, EmployeeNumber = "E1", HireDate = new DateOnly(2020, 1, 1), EmploymentStatusId = employmentStatusId });

        var legalEntities = new Mock<ILegalEntityRepository>();
        legalEntities.Setup(x => x.GetPrimaryByTenantIdAsync(TenantId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new LegalEntity { Id = LegalEntityId, TenantId = TenantId, IsPrimary = true, Name = "Acme" });

        var auditLogs = new Mock<IAuditLogRepository>();
        var fileStorage = new Mock<IFileStorageService>();
        var unitOfWork = new Mock<IUnitOfWork>();
        unitOfWork.Setup(x => x.SaveChangesAsync(It.IsAny<CancellationToken>())).ReturnsAsync(1);

        var handler = new CreateProjectCommandHandler(
            currentUser.Object, categories.Object, projects.Object, objectives.Object, members.Object,
            versions.Object, releaseCalendar.Object, labels.Object, entityAssets.Object, employees.Object,
            legalEntities.Object, auditLogs.Object, fileStorage.Object, unitOfWork.Object);

        return (handler, projects);
    }

    [Fact]
    public async Task Handle_ValidRequest_ReturnsSuccessWithDefaultObjectiveMembershipVersionAndReminder()
    {
        var (handler, _) = BuildHandler();

        var result = await handler.Handle(ValidCommand(), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal("Website Revamp", result.Value!.Project.Name);
        Assert.True(result.Value.DefaultObjective.IsDefault);
        Assert.Equal(result.Value.Project.Id, result.Value.DefaultObjective.ProjectId);
        Assert.Equal(1, result.Value.DefaultVersion.StatusId);
        Assert.Equal(result.Value.DefaultVersion.Id, result.Value.ReleaseReminder.VersionId);
        Assert.Equal(EmployeeId, result.Value.CreatorMembership.EmployeeId);
        Assert.Equal(result.Value.DefaultObjective.Id, result.Value.CreatorMembership.ObjectiveId);
    }

    [Fact]
    public async Task Handle_DuplicateIdentifier_ReturnsConflict()
    {
        var (handler, _) = BuildHandler(identifierExists: true);

        var result = await handler.Handle(ValidCommand(), CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(409, result.StatusCode);
    }

    [Fact]
    public async Task Handle_CategoryNotFoundForTenant_ReturnsNotFound()
    {
        var (handler, _) = BuildHandler(categoryExists: false);

        var result = await handler.Handle(ValidCommand(), CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(404, result.StatusCode);
    }

    [Fact]
    public async Task Handle_AllocatedHoursExceedActualHours_StillSucceeds_OverAllocationIsWarningOnly()
    {
        var (handler, _) = BuildHandler();
        var command = ValidCommand() with { ActualHours = 5m, DefaultObjectiveAllocatedHours = 999m };

        var result = await handler.Handle(command, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(999m, result.Value!.DefaultObjective.AllocatedHours);
    }

    [Fact]
    public async Task Handle_DuplicateLabelNamesInRequest_ReturnsValidationConflict()
    {
        var (handler, _) = BuildHandler();
        var command = ValidCommand([new CreateProjectLabelInput("Backend", "#111111"), new CreateProjectLabelInput("backend", "#222222")]);

        var result = await handler.Handle(command, CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(409, result.StatusCode);
    }

    [Fact]
    public async Task Handle_CallerEmployeeNotActive_ReturnsForbidden()
    {
        var (handler, _) = BuildHandler(employmentStatusId: 4); // 4 = terminated, per EmploymentStatusIds precedent

        var result = await handler.Handle(ValidCommand(), CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(403, result.StatusCode);
    }
}
