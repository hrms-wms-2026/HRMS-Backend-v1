using FluentAssertions;
using Moq;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Common.RepositoryInterfaces;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.Auth.Permission.ServiceInterfaces;
using ONEVO.Application.Features.Storage.File.DTOs.Responses;
using ONEVO.Application.Features.Storage.File.ServiceInterfaces;
using ONEVO.Application.Features.WorkManagement.Common.Services;
using ONEVO.Application.Features.WorkManagement.Projects.Queries.GetProjectBanner;
using ONEVO.Application.Features.WorkManagement.Projects.RepositoryInterfaces;
using ONEVO.Application.Features.WorkManagement.ProjectMembers.RepositoryInterfaces;
using ONEVO.Domain.Features.WorkManagement.Projects.Entities;
using Xunit;

namespace ONEVO.Tests.Unit.Features.WorkManagement;

public class GetProjectBannerQueryHandlerTests
{
    private readonly Mock<IProjectRepository> _projects = new();
    private readonly Mock<IEntityAssetRepository> _entityAssets = new();
    private readonly Mock<IProjectMemberRepository> _members = new();
    private readonly Mock<IPermissionResolver> _permissionResolver = new();
    private readonly Mock<ICurrentUser> _currentUser = new();
    private readonly Mock<ICallerIdentityResolver> _identity = new();
    private readonly Mock<IFileStorageService> _fileStorage = new();

    private static readonly Guid TenantId = Guid.NewGuid();
    private static readonly Guid UserId = Guid.NewGuid();
    private static readonly Guid EmployeeId = Guid.NewGuid();

    private static Project MakeProject(Guid id) => new()
    {
        Id = id, TenantId = TenantId, LeadId = Guid.NewGuid(), IsActive = true,
        Name = "P", Identifier = "P1", CreatedAt = DateTimeOffset.UtcNow
    };

    private void AuthenticateCurrentUser()
    {
        _currentUser.SetupGet(c => c.IsAuthenticated).Returns(true);
        _currentUser.SetupGet(c => c.TenantId).Returns(TenantId);
        _currentUser.SetupGet(c => c.UserId).Returns(UserId);
        _identity.Setup(x => x.ResolveCallerEmployeeIdAsync(TenantId, UserId, It.IsAny<CancellationToken>())).ReturnsAsync(EmployeeId);
    }

    private GetProjectBannerQueryHandler BuildHandler() =>
        new(_projects.Object, _entityAssets.Object, _members.Object, _permissionResolver.Object, _currentUser.Object, _identity.Object, _fileStorage.Object);

    [Fact]
    public async Task Handle_ProjectNotFound_ReturnsNotFound()
    {
        AuthenticateCurrentUser();
        var id = Guid.NewGuid();
        _projects.Setup(r => r.GetByIdForTenantAsync(TenantId, id, It.IsAny<CancellationToken>()))
            .ReturnsAsync((Project?)null);

        var result = await BuildHandler().Handle(new GetProjectBannerQuery(id), CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.StatusCode.Should().Be(404);
        _fileStorage.Verify(f => f.OpenReadAsync(It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Handle_NoPermissionAndNotMember_ReturnsForbidden()
    {
        AuthenticateCurrentUser();
        var project = MakeProject(Guid.NewGuid());
        _projects.Setup(r => r.GetByIdForTenantAsync(TenantId, project.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(project);
        _permissionResolver.Setup(r => r.ResolveAsync(UserId, TenantId, It.IsAny<Guid?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<string>());
        _members.Setup(r => r.HasActiveMembershipAsync(TenantId, project.Id, EmployeeId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);

        var result = await BuildHandler().Handle(new GetProjectBannerQuery(project.Id), CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.StatusCode.Should().Be(403);
        _fileStorage.Verify(f => f.OpenReadAsync(It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Handle_NoBannerAsset_ReturnsNotFound()
    {
        AuthenticateCurrentUser();
        var project = MakeProject(Guid.NewGuid());
        _projects.Setup(r => r.GetByIdForTenantAsync(TenantId, project.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(project);
        _permissionResolver.Setup(r => r.ResolveAsync(UserId, TenantId, It.IsAny<Guid?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<string> { "projects:read" });
        _entityAssets.Setup(r => r.GetPrimaryFileIdsByOwnerAsync(
                TenantId, "project", It.Is<IReadOnlyCollection<Guid>>(ids => ids.Contains(project.Id)), "project_banner", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Dictionary<Guid, Guid>());

        var result = await BuildHandler().Handle(new GetProjectBannerQuery(project.Id), CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.StatusCode.Should().Be(404);
    }

    [Fact]
    public async Task Handle_BannerAssetExists_DelegatesToFileStorageWithTheStoredFileId()
    {
        AuthenticateCurrentUser();
        var project = MakeProject(Guid.NewGuid());
        var fileId = Guid.NewGuid();
        _projects.Setup(r => r.GetByIdForTenantAsync(TenantId, project.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(project);
        _permissionResolver.Setup(r => r.ResolveAsync(UserId, TenantId, It.IsAny<Guid?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<string> { "projects:read" });
        _entityAssets.Setup(r => r.GetPrimaryFileIdsByOwnerAsync(
                TenantId, "project", It.Is<IReadOnlyCollection<Guid>>(ids => ids.Contains(project.Id)), "project_banner", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Dictionary<Guid, Guid> { [project.Id] = fileId });
        using var stream = new MemoryStream();
        _fileStorage.Setup(f => f.OpenReadAsync(TenantId, fileId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<FileStreamDto>.Success(new FileStreamDto(stream, "image/png")));

        var result = await BuildHandler().Handle(new GetProjectBannerQuery(project.Id), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value!.ContentType.Should().Be("image/png");
        _fileStorage.Verify(f => f.OpenReadAsync(TenantId, fileId, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Handle_NoPermissionButActiveMember_Succeeds()
    {
        AuthenticateCurrentUser();
        var project = MakeProject(Guid.NewGuid());
        var fileId = Guid.NewGuid();
        _projects.Setup(r => r.GetByIdForTenantAsync(TenantId, project.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(project);
        _permissionResolver.Setup(r => r.ResolveAsync(UserId, TenantId, It.IsAny<Guid?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<string>());
        _members.Setup(r => r.HasActiveMembershipAsync(TenantId, project.Id, EmployeeId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);
        _entityAssets.Setup(r => r.GetPrimaryFileIdsByOwnerAsync(
                TenantId, "project", It.Is<IReadOnlyCollection<Guid>>(ids => ids.Contains(project.Id)), "project_banner", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Dictionary<Guid, Guid> { [project.Id] = fileId });
        using var stream = new MemoryStream();
        _fileStorage.Setup(f => f.OpenReadAsync(TenantId, fileId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<FileStreamDto>.Success(new FileStreamDto(stream, "image/png")));

        var result = await BuildHandler().Handle(new GetProjectBannerQuery(project.Id), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
    }
}
