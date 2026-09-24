using Moq;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.Auth.Permission.RepositoryInterfaces;
using ONEVO.Infrastructure.Security;
using Xunit;

namespace ONEVO.Tests.Unit.Features.Auth;

public sealed class PermissionResolverTests
{
    private readonly Mock<IPermissionRepository> _permissions = new();
    private readonly Mock<IUserPermissionOverrideRepository> _permissionOverrides = new();
    private readonly Mock<IModuleEntitlementService> _entitlements = new();
    private readonly Mock<IDateTimeProvider> _clock = new();

    public PermissionResolverTests()
    {
        _clock.Setup(c => c.UtcNow).Returns(new DateTimeOffset(2026, 8, 16, 12, 0, 0, TimeSpan.Zero));
    }

    [Fact]
    public async Task ResolveAsync_PassesActiveLegalEntityIdThroughToRepository()
    {
        var userId = Guid.NewGuid();
        var tenantId = Guid.NewGuid();
        var legalEntityId = Guid.NewGuid();

        _permissions.Setup(p => p.UserHasPermissionCodeAsync(userId, "*", It.IsAny<DateTimeOffset>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);
        _entitlements.Setup(e => e.GetActiveModuleKeysForTenantAsync(tenantId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<string> { "core_hr" });
        _permissionOverrides.Setup(o => o.ListForUserAsync(tenantId, userId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<UserPermissionOverrideGrant>());
        _permissions
            .Setup(p => p.ListRolePermissionCodesWithModulesAsync(userId, It.IsAny<DateTimeOffset>(), legalEntityId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<PermissionCodeWithModule> { new("employees:read", "core_hr") });

        var resolver = new PermissionResolver(_permissions.Object, _permissionOverrides.Object, _entitlements.Object, _clock.Object);
        var result = await resolver.ResolveAsync(userId, tenantId, legalEntityId, CancellationToken.None);

        Assert.Contains("employees:read", result);
        _permissions.Verify(
            p => p.ListRolePermissionCodesWithModulesAsync(userId, It.IsAny<DateTimeOffset>(), legalEntityId, It.IsAny<CancellationToken>()),
            Times.Once);
    }
}
