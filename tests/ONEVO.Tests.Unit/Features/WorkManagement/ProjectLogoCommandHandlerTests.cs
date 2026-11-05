using Moq;
using ONEVO.Application.Common.Constants;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Common.RepositoryInterfaces;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.Storage.EntityAssets.ServiceInterfaces;
using ONEVO.Application.Features.Storage.File.Helpers;
using ONEVO.Application.Features.WorkManagement.Common.Services;
using ONEVO.Application.Features.WorkManagement.Projects.Commands.LinkProjectLogo;
using ONEVO.Application.Features.WorkManagement.Projects.Commands.RemoveProjectLogo;
using ONEVO.Application.Features.WorkManagement.Projects.RepositoryInterfaces;
using ONEVO.Domain.Features.WorkManagement.Projects.Entities;
using Xunit;

namespace ONEVO.Tests.Unit.Features.WorkManagement;

public class ProjectLogoCommandHandlerTests
{
    private static readonly Guid TenantId = Guid.NewGuid();
    private static readonly Guid UserId = Guid.NewGuid();
    private static readonly Guid EmployeeId = Guid.NewGuid();
    private static readonly Guid ProjectId = Guid.NewGuid();
    private static readonly Guid FileId = Guid.NewGuid();

    private readonly Mock<ICurrentUser> _currentUser = new();
    private readonly Mock<ICallerIdentityResolver> _identity = new();
    private readonly Mock<IProjectRepository> _projects = new();
    private readonly Mock<IPrimaryEntityAssetLinker> _linker = new();
    private readonly Mock<IUnitOfWork> _unitOfWork = new();

    public ProjectLogoCommandHandlerTests()
    {
        _currentUser.SetupGet(x => x.IsAuthenticated).Returns(true);
        _currentUser.SetupGet(x => x.TenantId).Returns(TenantId);
        _currentUser.SetupGet(x => x.UserId).Returns(UserId);
        _identity.Setup(x => x.ResolveCallerEmployeeIdAsync(TenantId, UserId, It.IsAny<CancellationToken>())).ReturnsAsync(EmployeeId);
        _linker.Setup(x => x.LinkAsync(TenantId, UserId, EntityAssetOwnerTypes.Project, ProjectId, UploadPurposeCatalog.ProjectCover, FileId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<Guid?>.Success(FileId));
        _linker.Setup(x => x.RemoveAsync(TenantId, UserId, EntityAssetOwnerTypes.Project, ProjectId, UploadPurposeCatalog.ProjectCover, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Success());
    }

    private void GivenProject(Project project)
    {
        _projects.Setup(x => x.GetTrackedByIdForTenantAsync(TenantId, ProjectId, It.IsAny<CancellationToken>())).ReturnsAsync(project);
        _projects.Setup(x => x.GetByIdForTenantAsync(TenantId, ProjectId, It.IsAny<CancellationToken>())).ReturnsAsync(project);
    }

    private static Project ExistingProject(Guid? leadId = null) => new()
    {
        Id = ProjectId, TenantId = TenantId, CategoryId = Guid.NewGuid(), Name = "Website Revamp", Identifier = "WEB",
        LeadId = leadId ?? EmployeeId, StartDate = new DateOnly(2026, 1, 1), TargetDate = new DateOnly(2026, 6, 1),
        IsActive = true, Icon = "lucide:rocket", CreatedAt = DateTimeOffset.UtcNow
    };

    private LinkProjectLogoCommandHandler LinkHandler() =>
        new(_currentUser.Object, _identity.Object, _projects.Object, _linker.Object, _unitOfWork.Object);

    private RemoveProjectLogoCommandHandler RemoveHandler() =>
        new(_currentUser.Object, _identity.Object, _projects.Object, _linker.Object);

    [Fact]
    public async Task Link_ByLead_LinksTheFileAndClearsTheIcon()
    {
        var project = ExistingProject();
        GivenProject(project);

        var result = await LinkHandler().Handle(new LinkProjectLogoCommand(ProjectId, FileId), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(FileId, result.Value!.LogoFileId);
        Assert.Null(project.Icon);
        _unitOfWork.Verify(x => x.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Link_WhenLinkingFails_KeepsTheIcon()
    {
        var project = ExistingProject();
        GivenProject(project);
        _linker.Setup(x => x.LinkAsync(TenantId, UserId, EntityAssetOwnerTypes.Project, ProjectId, UploadPurposeCatalog.ProjectCover, FileId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<Guid?>.NotFound("File not found."));

        var result = await LinkHandler().Handle(new LinkProjectLogoCommand(ProjectId, FileId), CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(404, result.StatusCode);
        Assert.Equal("lucide:rocket", project.Icon);
    }

    [Fact]
    public async Task Link_ByNonLead_IsForbidden()
    {
        GivenProject(ExistingProject(leadId: Guid.NewGuid()));

        var result = await LinkHandler().Handle(new LinkProjectLogoCommand(ProjectId, FileId), CancellationToken.None);

        Assert.Equal(403, result.StatusCode);
        _linker.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task Remove_ByLead_RemovesTheLogo()
    {
        GivenProject(ExistingProject());

        var result = await RemoveHandler().Handle(new RemoveProjectLogoCommand(ProjectId), CancellationToken.None);

        Assert.True(result.IsSuccess);
        _linker.Verify(x => x.RemoveAsync(TenantId, UserId, EntityAssetOwnerTypes.Project, ProjectId, UploadPurposeCatalog.ProjectCover, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Remove_ByNonLead_IsForbidden()
    {
        GivenProject(ExistingProject(leadId: Guid.NewGuid()));

        var result = await RemoveHandler().Handle(new RemoveProjectLogoCommand(ProjectId), CancellationToken.None);

        Assert.Equal(403, result.StatusCode);
        _linker.VerifyNoOtherCalls();
    }
}
