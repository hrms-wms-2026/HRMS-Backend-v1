using Moq;
using ONEVO.Application.Features.Auth.Permission.RepositoryInterfaces;
using ONEVO.Application.Features.Auth.Permission.ServiceInterfaces;
using ONEVO.Application.Features.WorkManagement.Objectives.Services;
using ONEVO.Domain.Features.Auth.Entities;
using Xunit;

namespace ONEVO.Tests.Unit.Features.WorkManagement;

public class PermissionAutoGrantServiceTests
{
    private static readonly Guid TenantId = Guid.NewGuid();
    private static readonly Guid UserId = Guid.NewGuid();
    private static readonly Guid GrantedByUserId = Guid.NewGuid();
    private static readonly Guid PermissionId = Guid.NewGuid();

    private (PermissionAutoGrantService Service, Mock<IUserPermissionOverrideRepository> Overrides) BuildService(
        List<string> effectivePermissions, Permission? permission)
    {
        var resolver = new Mock<IPermissionResolver>();
        resolver.Setup(x => x.ResolveAsync(UserId, TenantId, It.IsAny<Guid?>(), It.IsAny<CancellationToken>())).ReturnsAsync(effectivePermissions);

        var permissions = new Mock<IPermissionRepository>();
        permissions.Setup(x => x.GetByCodeAsync("projects:access", It.IsAny<CancellationToken>())).ReturnsAsync(permission);

        var overrides = new Mock<IUserPermissionOverrideRepository>();

        var service = new PermissionAutoGrantService(resolver.Object, permissions.Object, overrides.Object);
        return (service, overrides);
    }

    [Fact]
    public async Task EnsureGrantedAsync_AlreadyHasPermission_DoesNothing()
    {
        var (service, overrides) = BuildService(["projects:access"], new Permission { Id = PermissionId, Code = "projects:access" });

        await service.EnsureGrantedAsync(TenantId, UserId, GrantedByUserId, "projects:access", CancellationToken.None);

        overrides.Verify(x => x.AddAsync(It.IsAny<UserPermissionOverride>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task EnsureGrantedAsync_HasWildcard_DoesNothing()
    {
        var (service, overrides) = BuildService(["*"], new Permission { Id = PermissionId, Code = "projects:access" });

        await service.EnsureGrantedAsync(TenantId, UserId, GrantedByUserId, "projects:access", CancellationToken.None);

        overrides.Verify(x => x.AddAsync(It.IsAny<UserPermissionOverride>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task EnsureGrantedAsync_MissingPermission_AddsGrantOverride()
    {
        var (service, overrides) = BuildService([], new Permission { Id = PermissionId, Code = "projects:access" });

        await service.EnsureGrantedAsync(TenantId, UserId, GrantedByUserId, "projects:access", CancellationToken.None);

        overrides.Verify(x => x.AddAsync(It.Is<UserPermissionOverride>(o =>
            o.TenantId == TenantId && o.UserId == UserId && o.PermissionId == PermissionId &&
            o.GrantType == "grant" && o.GrantedBy == GrantedByUserId), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task EnsureGrantedAsync_PermissionCodeNotSeeded_DoesNothingDefensively()
    {
        var (service, overrides) = BuildService([], null);

        await service.EnsureGrantedAsync(TenantId, UserId, GrantedByUserId, "projects:access", CancellationToken.None);

        overrides.Verify(x => x.AddAsync(It.IsAny<UserPermissionOverride>(), It.IsAny<CancellationToken>()), Times.Never);
    }
}
