using FluentAssertions;
using Moq;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.Auth.Login.Queries.GetCurrentSession;
using ONEVO.Domain.Features.InfrastructureModule.Entities;

namespace ONEVO.Tests.Unit.Features.Auth;

public sealed class GetCurrentSessionQueryHandlerTests
{
    [Fact]
    public async Task Me_ReturnsOnlyCurrentTenantSessionMetadata()
    {
        var tenantId = Guid.NewGuid();
        var expires = DateTimeOffset.UtcNow.AddMinutes(20);
        var userId = Guid.NewGuid();
        var current = new Mock<ICurrentUser>();
        current.SetupGet(instance => instance.IsAuthenticated).Returns(true);
        current.SetupGet(instance => instance.UserId).Returns(userId);
        current.SetupGet(instance => instance.TenantId).Returns(tenantId);
        current.SetupGet(instance => instance.Email).Returns("owner@acme.test");
        current.SetupGet(instance => instance.Permissions).Returns(new[] { "people.read" });
        current.SetupGet(instance => instance.SessionExpiresAt).Returns(expires);

        var tenant = new Mock<ITenantContext>();
        tenant.SetupGet(instance => instance.IsResolved).Returns(true);
        tenant.SetupGet(instance => instance.ContextMode).Returns(TenantContextMode.Tenant);
        tenant.SetupGet(instance => instance.TenantId).Returns(tenantId);

        var entitlements = new Mock<IModuleEntitlementService>();
        entitlements
            .Setup(instance => instance.GetActiveModuleKeysForTenantAsync(
                tenantId,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new[] { "people" });

        var handler = new GetCurrentSessionQueryHandler(
            current.Object,
            tenant.Object,
            entitlements.Object);

        var result = await handler.Handle(new GetCurrentSessionQuery(), default);

        result.IsSuccess.Should().BeTrue();
        result.Value!.Authenticated.Should().BeTrue();
        result.Value.User!.UserId.Should().Be(userId);
        result.Value.User.TenantId.Should().Be(tenantId);
        result.Value.Permissions.Should().Equal("people.read");
        result.Value.ActiveModules.Should().Equal("people");
        result.Value.ExpiresAt.Should().Be(expires);
    }
}
