using FluentAssertions;
using MediatR;
using Moq;
using ONEVO.Application.Common.Behaviors;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.DevPlatform.PlatformAccess.ServiceInterfaces;
using Xunit;

namespace ONEVO.Tests.Unit.Features.Application.Common.Behaviors;

public class AdminTenantContextBehaviorTests
{
    private readonly Mock<ICurrentPlatformUserContext> _currentPlatformUser = new();
    private readonly Mock<IWritableTenantContext> _tenantContext = new();

    public record TestRequest : IRequest<string>;

    private AdminTenantContextBehavior<TestRequest, string> BuildBehavior() =>
        new(_currentPlatformUser.Object, _tenantContext.Object);

    [Fact]
    public async Task Handle_AdminAuthenticatedRequest_SetsAdminModeBeforeCallingNext()
    {
        _currentPlatformUser.Setup(c => c.UserId).Returns(Guid.NewGuid());
        var nextCalled = false;
        RequestHandlerDelegate<string> next = _ =>
        {
            // Admin mode must already be set by the time the inner pipeline/handler runs.
            _tenantContext.Verify(t => t.SetAdminMode(), Times.Once);
            nextCalled = true;
            return Task.FromResult("ok");
        };

        var result = await BuildBehavior().Handle(new TestRequest(), next, CancellationToken.None);

        result.Should().Be("ok");
        nextCalled.Should().BeTrue();
        _tenantContext.Verify(t => t.SetAdminMode(), Times.Once);
    }

    [Fact]
    public async Task Handle_NonAdminRequest_DoesNotSetAdminMode()
    {
        _currentPlatformUser.Setup(c => c.UserId).Returns((Guid?)null);
        RequestHandlerDelegate<string> next = _ => Task.FromResult("ok");

        var result = await BuildBehavior().Handle(new TestRequest(), next, CancellationToken.None);

        result.Should().Be("ok");
        _tenantContext.Verify(t => t.SetAdminMode(), Times.Never);
    }
}
