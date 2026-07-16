using FluentAssertions;
using Moq;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.Auth.Permission.RepositoryInterfaces;
using ONEVO.Application.Features.Auth.Roles.RepositoryInterfaces;
using ONEVO.Domain.Features.Auth.Entities;
using ONEVO.Infrastructure.Security;
using Xunit;

namespace ONEVO.Tests.Unit.Features.Auth;

public sealed class PermissionResolverModuleAutoGrantTests
{
    private readonly Mock<IPermissionRepository> _permissions = new();
    private readonly Mock<IUserPermissionOverrideRepository> _overrides = new();
    private readonly Mock<IModuleEntitlementService> _entitlements = new();
    private readonly Mock<IDateTimeProvider> _clock = new();

    private static readonly Guid UserId = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
    private static readonly Guid TenantId = Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb");
    private static readonly DateTimeOffset Now = new(2026, 5, 20, 10, 0, 0, TimeSpan.Zero);

    public PermissionResolverModuleAutoGrantTests()
    {
        _clock.Setup(c => c.UtcNow).Returns(Now);
        _permissions
            .Setup(p => p.UserHasPermissionCodeAsync(UserId, "*", Now, It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);
        _permissions
            .Setup(p => p.ListRolePermissionCodesWithModulesAsync(UserId, Now, It.IsAny<CancellationToken>()))
            .ReturnsAsync([]);
        _overrides
            .Setup(o => o.ListForUserAsync(TenantId, UserId, It.IsAny<CancellationToken>()))
            .ReturnsAsync([]);
    }

    private PermissionResolver Sut =>
        new(_permissions.Object, _overrides.Object, _entitlements.Object, _clock.Object);

    [Fact]
    public async Task ResolveAsync_LeaveModuleActive_GrantsLeaveReadOwn()
    {
        _entitlements
            .Setup(e => e.GetActiveModuleKeysForTenantAsync(TenantId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(["leave"]);

        var result = await Sut.ResolveAsync(UserId, TenantId, CancellationToken.None);

        result.Should().Contain("leave:read-own");
        result.Should().NotContain("attendance:read-own");
        result.Should().NotContain("inbox:read");
    }

    [Fact]
    public async Task ResolveAsync_AttendanceModuleActive_GrantsBothAttendanceCodes()
    {
        _entitlements
            .Setup(e => e.GetActiveModuleKeysForTenantAsync(TenantId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(["attendance"]);

        var result = await Sut.ResolveAsync(UserId, TenantId, CancellationToken.None);

        result.Should().Contain("attendance:read-own");
        result.Should().Contain("attendance:write-own");
    }

    [Fact]
    public async Task ResolveAsync_NoActiveModules_ResultIsEmpty()
    {
        _entitlements
            .Setup(e => e.GetActiveModuleKeysForTenantAsync(TenantId, It.IsAny<CancellationToken>()))
            .ReturnsAsync([]);

        var result = await Sut.ResolveAsync(UserId, TenantId, CancellationToken.None);

        result.Should().BeEmpty();
    }

    [Fact]
    public async Task ResolveAsync_RoleHasLeaveApprove_DerivesInboxRead()
    {
        _entitlements
            .Setup(e => e.GetActiveModuleKeysForTenantAsync(TenantId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(["leave"]);
        _permissions
            .Setup(p => p.ListRolePermissionCodesWithModulesAsync(UserId, Now, It.IsAny<CancellationToken>()))
            .ReturnsAsync([new PermissionCodeWithModule("leave:approve", "leave")]);

        var result = await Sut.ResolveAsync(UserId, TenantId, CancellationToken.None);

        result.Should().Contain("inbox:read");
        result.Should().Contain("notifications:read");
    }

    [Fact]
    public async Task ResolveAsync_RoleHasNoTriggerPerms_NoInboxOrNotifications()
    {
        _entitlements
            .Setup(e => e.GetActiveModuleKeysForTenantAsync(TenantId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(["employees", "leave"]);
        _permissions
            .Setup(p => p.ListRolePermissionCodesWithModulesAsync(UserId, Now, It.IsAny<CancellationToken>()))
            .ReturnsAsync([new PermissionCodeWithModule("employees:read", "employees")]);

        var result = await Sut.ResolveAsync(UserId, TenantId, CancellationToken.None);

        result.Should().NotContain("inbox:read");
        result.Should().NotContain("notifications:read");
        result.Should().Contain("employees:read");
        result.Should().Contain("employees:read-own");
        result.Should().Contain("leave:read-own");
    }

    [Fact]
    public async Task ResolveAsync_OverrideRevoke_CannotRemoveModuleAutoGrant()
    {
        _entitlements
            .Setup(e => e.GetActiveModuleKeysForTenantAsync(TenantId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(["employees"]);
        _overrides
            .Setup(o => o.ListForUserAsync(TenantId, UserId, It.IsAny<CancellationToken>()))
            .ReturnsAsync([new UserPermissionOverrideGrant("employees:read-own", "revoke")]);

        var result = await Sut.ResolveAsync(UserId, TenantId, CancellationToken.None);

        result.Should().Contain("employees:read-own");
    }

    [Fact]
    public async Task ResolveAsync_Phase2ModuleNotActive_Phase2AutoGrantNotPresent()
    {
        _entitlements
            .Setup(e => e.GetActiveModuleKeysForTenantAsync(TenantId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(["employees"]);

        var result = await Sut.ResolveAsync(UserId, TenantId, CancellationToken.None);

        result.Should().NotContain("payroll:read-own");
        result.Should().NotContain("performance:read-own");
        result.Should().NotContain("documents:read-own");
    }

    [Fact]
    public async Task ResolveAsync_RolePermissionModuleNotActive_IsExcluded()
    {
        _entitlements
            .Setup(e => e.GetActiveModuleKeysForTenantAsync(TenantId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(["employees"]);
        _permissions
            .Setup(p => p.ListRolePermissionCodesWithModulesAsync(UserId, Now, It.IsAny<CancellationToken>()))
            .ReturnsAsync([new PermissionCodeWithModule("payroll:read", "payroll")]);

        var result = await Sut.ResolveAsync(UserId, TenantId, CancellationToken.None);

        result.Should().NotContain("payroll:read");
    }
}
