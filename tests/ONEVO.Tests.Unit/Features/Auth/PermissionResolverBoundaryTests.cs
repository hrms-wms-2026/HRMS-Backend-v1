using FluentAssertions;
using Moq;
using ONEVO.Application.Common.RepositoryInterfaces;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.Auth.Invite.RepositoryInterfaces;
using ONEVO.Application.Features.Auth.Login.RepositoryInterfaces;
using ONEVO.Application.Features.Auth.Permission.RepositoryInterfaces;
using ONEVO.Application.Features.Auth.Roles.RepositoryInterfaces;
using ONEVO.Domain.Features.Auth.Entities;
using ONEVO.Infrastructure.Security;
using Xunit;

namespace ONEVO.Tests.Unit.Features.Auth;

public sealed class PermissionResolverBoundaryTests
{
    private readonly Mock<IPermissionRepository> _permissions = new();
    private readonly Mock<IUserPermissionOverrideRepository> _overrides = new();
    private readonly Mock<IModuleEntitlementService> _entitlements = new();
    private readonly Mock<IDateTimeProvider> _clock = new();

    private static readonly Guid UserId = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid TenantId = Guid.Parse("22222222-2222-2222-2222-222222222222");
    private static readonly DateTimeOffset Now = new(2026, 5, 10, 12, 0, 0, TimeSpan.Zero);

    public PermissionResolverBoundaryTests()
    {
        _clock.Setup(c => c.UtcNow).Returns(Now);
    }

    private PermissionResolver Sut =>
        new(_permissions.Object, _overrides.Object, _entitlements.Object, _clock.Object);

    private void SetupNoWildcard()
    {
        _permissions
            .Setup(p => p.UserHasPermissionCodeAsync(UserId, "*", Now, It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);
    }

    [Fact]
    public async Task ResolveAsync_WildcardOnRole_StillReturnsStarOnly()
    {
        _permissions
            .Setup(p => p.UserHasPermissionCodeAsync(UserId, "*", Now, It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        var result = await Sut.ResolveAsync(UserId, TenantId, CancellationToken.None);

        result.Should().Equal("*");
        _entitlements.Verify(
            e => e.GetActiveModuleKeysForTenantAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task ResolveAsync_RolePermissionOutsideActiveModules_IsExcluded()
    {
        SetupNoWildcard();
        _entitlements
            .Setup(e => e.GetActiveModuleKeysForTenantAsync(TenantId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<string> { "employees" });

        _permissions
            .Setup(p => p.ListRolePermissionCodesWithModulesAsync(UserId, Now, It.IsAny<CancellationToken>()))
            .ReturnsAsync(
            [
                new PermissionCodeWithModule("payroll:read", "payroll"),
                new PermissionCodeWithModule("employees:read", "employees")
            ]);

        _overrides
            .Setup(o => o.ListForUserAsync(TenantId, UserId, It.IsAny<CancellationToken>()))
            .ReturnsAsync([]);

        var result = await Sut.ResolveAsync(UserId, TenantId, CancellationToken.None);

        result.Should().Contain("employees:read");
        result.Should().NotContain("payroll:read");
        // inbox:read is derived, not universal — no trigger perms here so it must be absent
        result.Should().NotContain("inbox:read");
    }

    [Fact]
    public async Task ResolveAsync_OverrideGrantOutsideActiveModules_IsExcluded()
    {
        SetupNoWildcard();
        _entitlements
            .Setup(e => e.GetActiveModuleKeysForTenantAsync(TenantId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<string> { "employees" });

        _permissions
            .Setup(p => p.ListRolePermissionCodesWithModulesAsync(UserId, Now, It.IsAny<CancellationToken>()))
            .ReturnsAsync([]);

        _overrides
            .Setup(o => o.ListForUserAsync(TenantId, UserId, It.IsAny<CancellationToken>()))
            .ReturnsAsync([new UserPermissionOverrideGrant("payroll:write", "grant")]);

        _permissions
            .Setup(p => p.GetByCodesAsync(It.IsAny<IEnumerable<string>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(
            [
                new Permission
                {
                    Id = Guid.NewGuid(),
                    Code = "payroll:write",
                    Module = "payroll",
                    Description = ""
                }
            ]);

        var result = await Sut.ResolveAsync(UserId, TenantId, CancellationToken.None);

        result.Should().NotContain("payroll:write");
    }

    [Fact]
    public async Task ResolveAsync_OverrideRevoke_DoesNotRemoveModuleAutoGrant()
    {
        SetupNoWildcard();
        _entitlements
            .Setup(e => e.GetActiveModuleKeysForTenantAsync(TenantId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<string> { "employees" });

        _permissions
            .Setup(p => p.ListRolePermissionCodesWithModulesAsync(UserId, Now, It.IsAny<CancellationToken>()))
            .ReturnsAsync([]);

        _overrides
            .Setup(o => o.ListForUserAsync(TenantId, UserId, It.IsAny<CancellationToken>()))
            .ReturnsAsync([new UserPermissionOverrideGrant("employees:read-own", "revoke")]);

        var result = await Sut.ResolveAsync(UserId, TenantId, CancellationToken.None);

        // employees:read-own is a module auto-grant — revoke overrides cannot remove it
        result.Should().Contain("employees:read-own");
    }

    [Fact]
    public async Task ResolveAsync_NoActiveModules_ResultIsEmpty()
    {
        SetupNoWildcard();
        _entitlements
            .Setup(e => e.GetActiveModuleKeysForTenantAsync(TenantId, It.IsAny<CancellationToken>()))
            .ReturnsAsync([]);

        _permissions
            .Setup(p => p.ListRolePermissionCodesWithModulesAsync(UserId, Now, It.IsAny<CancellationToken>()))
            .ReturnsAsync([new PermissionCodeWithModule("employees:read", "employees")]);

        _overrides
            .Setup(o => o.ListForUserAsync(TenantId, UserId, It.IsAny<CancellationToken>()))
            .ReturnsAsync([]);

        var result = await Sut.ResolveAsync(UserId, TenantId, CancellationToken.None);

        result.Should().BeEmpty();
    }
}
