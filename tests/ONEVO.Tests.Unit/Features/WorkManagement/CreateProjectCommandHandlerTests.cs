using Moq;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Common.RepositoryInterfaces;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.Auth.Login.RepositoryInterfaces;
using ONEVO.Application.Features.OrgStructure.RepositoryInterfaces;
using ONEVO.Application.Features.Storage.File.DTOs.Responses;
using ONEVO.Application.Features.Storage.File.Helpers;
using ONEVO.Application.Features.Storage.File.ServiceInterfaces;
using ONEVO.Application.Features.WorkManagement.Objectives.RepositoryInterfaces;
using ONEVO.Application.Features.WorkManagement.Projects.Commands.CreateProject;
using ONEVO.Application.Features.WorkManagement.Projects.DTOs.Requests;
using ONEVO.Application.Features.WorkManagement.Projects.RepositoryInterfaces;
using ONEVO.Application.Features.WorkManagement.ProjectMembers.RepositoryInterfaces;
using ONEVO.Application.Features.WorkManagement.ReleaseCalendar.RepositoryInterfaces;
using ONEVO.Application.Features.WorkManagement.Versions.RepositoryInterfaces;
using ONEVO.Application.Features.WorkManagement.Labels.RepositoryInterfaces;
using ONEVO.Application.Features.WorkManagement.Tasks.RepositoryInterfaces;
using ONEVO.Domain.Features.CoreHr.Entities;
using ONEVO.Domain.Features.OrgStructure.Entities;
using ONEVO.Domain.Features.Storage.EntityAssets.Entities;
using ONEVO.Domain.Features.WorkManagement.Projects.Entities;
using ONEVO.Domain.Features.WorkManagement.ReleaseCalendar.Entities;
using ONEVO.Domain.Lookups;
using Xunit;
using TaskStatusEntity = ONEVO.Domain.Features.WorkManagement.Tasks.Entities.TaskStatus;

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

    private sealed record HandlerSetup(
        CreateProjectCommandHandler Handler,
        Mock<IProjectRepository> Projects,
        Mock<ITaskStatusRepository> TaskStatuses,
        Mock<IReleaseCalendarRepository> ReleaseCalendar,
        Mock<IEntityAssetRepository> EntityAssets,
        Mock<IFileStorageService> FileStorage);

    private HandlerSetup BuildHandler(
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
        var taskStatuses = new Mock<ITaskStatusRepository>();
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
            versions.Object, releaseCalendar.Object, labels.Object, taskStatuses.Object, entityAssets.Object, employees.Object,
            legalEntities.Object, auditLogs.Object, fileStorage.Object, unitOfWork.Object);

        return new HandlerSetup(handler, projects, taskStatuses, releaseCalendar, entityAssets, fileStorage);
    }

    [Fact]
    public async Task Handle_ValidRequest_ReturnsSuccessWithDefaultObjectiveMembershipVersionAndReminder()
    {
        var handler = BuildHandler().Handler;

        var result = await handler.Handle(ValidCommand(), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal("Website Revamp", result.Value!.Project.Name);
        Assert.True(result.Value.DefaultObjective.IsDefault);
        Assert.Equal(result.Value.Project.Id, result.Value.DefaultObjective.ProjectId);
        Assert.Equal(1, result.Value.DefaultVersion.StatusId);
        Assert.Equal(result.Value.DefaultVersion.Id, result.Value.ReleaseReminder.VersionId);
        Assert.Equal(EmployeeId, result.Value.CreatorMembership.UserId);
        Assert.Equal(result.Value.DefaultObjective.Id, result.Value.CreatorMembership.ObjectiveId);
    }

    [Fact]
    public async Task Handle_ValidRequest_SeedsProjectAndDefaultObjectiveTaskStatuses()
    {
        var setup = BuildHandler();

        var result = await setup.Handler.Handle(ValidCommand(), CancellationToken.None);

        Assert.True(result.IsSuccess);
        var defaultObjectiveId = result.Value!.DefaultObjective.Id;
        setup.TaskStatuses.Verify(x => x.AddRangeAsync(
            It.Is<IReadOnlyList<TaskStatusEntity>>(rows =>
                rows.Count == 4 && rows.All(r => r.ObjectiveId == null)),
            It.IsAny<CancellationToken>()), Times.Once);
        setup.TaskStatuses.Verify(x => x.AddRangeAsync(
            It.Is<IReadOnlyList<TaskStatusEntity>>(rows =>
                rows.Count == 4 && rows.All(r => r.ObjectiveId == defaultObjectiveId)),
            It.IsAny<CancellationToken>()), Times.Once);
        setup.TaskStatuses.Verify(x => x.AddRangeAsync(
            It.IsAny<IReadOnlyList<TaskStatusEntity>>(),
            It.IsAny<CancellationToken>()), Times.Exactly(2));
    }

    [Fact]
    public async Task Handle_DuplicateIdentifier_ReturnsConflict()
    {
        var handler = BuildHandler(identifierExists: true).Handler;

        var result = await handler.Handle(ValidCommand(), CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(409, result.StatusCode);
    }

    [Fact]
    public async Task Handle_CategoryNotFoundForTenant_ReturnsNotFound()
    {
        var handler = BuildHandler(categoryExists: false).Handler;

        var result = await handler.Handle(ValidCommand(), CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(404, result.StatusCode);
    }

    [Fact]
    public async Task Handle_AllocatedHoursExceedActualHours_StillSucceeds_OverAllocationIsWarningOnly()
    {
        var handler = BuildHandler().Handler;
        var command = ValidCommand() with { ActualHours = 5m, DefaultObjectiveAllocatedHours = 999m };

        var result = await handler.Handle(command, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(999m, result.Value!.DefaultObjective.AllocatedHours);
    }

    [Fact]
    public async Task Handle_DuplicateLabelNamesInRequest_ReturnsValidationConflict()
    {
        var handler = BuildHandler().Handler;
        var command = ValidCommand([new CreateProjectLabelInput("Backend", "#111111"), new CreateProjectLabelInput("backend", "#222222")]);

        var result = await handler.Handle(command, CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(409, result.StatusCode);
    }

    [Fact]
    public void CreateProjectCommand_AllowsOptionalReleaseDateAndBannerFields()
    {
        using var banner = new MemoryStream();
        var command = ValidCommand() with
        {
            ReleaseDate = null,
            BannerFileName = "banner.png",
            BannerContentType = "image/png",
            BannerContent = banner
        };

        Assert.Null(command.ReleaseDate);
        Assert.Equal("banner.png", command.BannerFileName);
        Assert.Equal("image/png", command.BannerContentType);
        Assert.Same(banner, command.BannerContent);
    }

    [Fact]
    public async Task Handle_OmittedReleaseDate_DefaultsScheduledDateToTargetDate()
    {
        var setup = BuildHandler();
        ReleaseCalendarEntry? captured = null;
        setup.ReleaseCalendar
            .Setup(x => x.AddAsync(It.IsAny<ReleaseCalendarEntry>(), It.IsAny<CancellationToken>()))
            .Callback<ReleaseCalendarEntry, CancellationToken>((entry, _) => captured = entry)
            .Returns(Task.CompletedTask);

        var command = ValidCommand() with { ReleaseDate = null };
        var result = await setup.Handler.Handle(command, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.NotNull(captured);
        Assert.Equal(command.TargetDate, captured!.ScheduledDate);
        Assert.Equal(command.TargetDate, result.Value!.ReleaseReminder.ScheduledDate);
    }

    private static FileRecordDto MakeFile(string name) => new(
        Guid.NewGuid(), TenantId, $"key/{name}", name, name, "image/png", 10, "abc", "completed", DateTimeOffset.UtcNow);

    private static void CaptureAssets(HandlerSetup setup, List<EntityAsset> assets)
    {
        setup.EntityAssets
            .Setup(x => x.AddAsync(It.IsAny<EntityAsset>(), It.IsAny<CancellationToken>()))
            .Callback<EntityAsset, CancellationToken>((asset, _) => assets.Add(asset))
            .Returns(Task.CompletedTask);
    }

    [Fact]
    public async Task Handle_LogoAndBannerUploaded_PersistsBothEntityAssetsWithDistinctPurposes()
    {
        var setup = BuildHandler();
        var assets = new List<EntityAsset>();
        CaptureAssets(setup, assets);
        using var logo = new MemoryStream(new byte[] { 1 });
        using var banner = new MemoryStream(new byte[] { 2 });
        setup.FileStorage
            .Setup(x => x.UploadAsync(TenantId, UserId, "logo.png", "image/png",
                UploadPurposeCatalog.ProjectCover, It.IsAny<Stream>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<FileRecordDto>.Success(MakeFile("logo.png")));
        setup.FileStorage
            .Setup(x => x.UploadAsync(TenantId, UserId, "banner.png", "image/png",
                UploadPurposeCatalog.ProjectBanner, It.IsAny<Stream>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<FileRecordDto>.Success(MakeFile("banner.png")));

        var command = ValidCommand() with
        {
            LogoFileName = "logo.png", LogoContentType = "image/png", LogoContent = logo,
            BannerFileName = "banner.png", BannerContentType = "image/png", BannerContent = banner
        };

        var result = await setup.Handler.Handle(command, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.NotNull(result.Value!.Logo);
        Assert.Equal("logo.png", result.Value.Logo.OriginalFileName);
        Assert.NotNull(result.Value.Banner);
        Assert.Equal("banner.png", result.Value.Banner.OriginalFileName);
        Assert.Equal(2, assets.Count);
        Assert.Contains(assets, a => a.AssetPurpose == UploadPurposeCatalog.ProjectCover && a.OwnerId == result.Value!.Project.Id && a.IsPrimary);
        Assert.Contains(assets, a => a.AssetPurpose == UploadPurposeCatalog.ProjectBanner && a.OwnerId == result.Value!.Project.Id && a.IsPrimary);
        Assert.Equal(2, assets.Select(a => a.FileRecordId).Distinct().Count());
    }

    [Fact]
    public async Task Handle_BannerOnly_PersistsBannerAssetAndNoLogoAsset()
    {
        var setup = BuildHandler();
        var assets = new List<EntityAsset>();
        CaptureAssets(setup, assets);
        using var banner = new MemoryStream(new byte[] { 2 });
        setup.FileStorage
            .Setup(x => x.UploadAsync(TenantId, UserId, "banner.png", "image/png",
                UploadPurposeCatalog.ProjectBanner, It.IsAny<Stream>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<FileRecordDto>.Success(MakeFile("banner.png")));

        var command = ValidCommand() with
        {
            BannerFileName = "banner.png", BannerContentType = "image/png", BannerContent = banner
        };

        var result = await setup.Handler.Handle(command, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Null(result.Value!.Logo);
        Assert.NotNull(result.Value.Banner);
        Assert.Equal("banner.png", result.Value.Banner.OriginalFileName);
        Assert.Single(assets);
        Assert.Equal(UploadPurposeCatalog.ProjectBanner, assets[0].AssetPurpose);
        Assert.Equal(result.Value!.Project.Id, assets[0].OwnerId);
        setup.FileStorage.Verify(x => x.UploadAsync(
            It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<string>(),
            UploadPurposeCatalog.ProjectCover, It.IsAny<Stream>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Handle_BannerUploadFailure_DoesNotCreateProject()
    {
        var setup = BuildHandler();
        using var banner = new MemoryStream(new byte[] { 2 });
        setup.FileStorage
            .Setup(x => x.UploadAsync(TenantId, UserId, "banner.png", "image/png",
                UploadPurposeCatalog.ProjectBanner, It.IsAny<Stream>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<FileRecordDto>.Failure("Invalid content type.", 400));

        var command = ValidCommand() with
        {
            BannerFileName = "banner.png", BannerContentType = "image/png", BannerContent = banner
        };

        var result = await setup.Handler.Handle(command, CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(400, result.StatusCode);
        setup.Projects.Verify(x => x.AddAsync(It.IsAny<Project>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Handle_CallerEmployeeNotActive_ReturnsForbidden()
    {
        var handler = BuildHandler(employmentStatusId: 4).Handler; // 4 = terminated, per EmploymentStatusIds precedent

        var result = await handler.Handle(ValidCommand(), CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(403, result.StatusCode);
    }
}
