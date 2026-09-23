using FluentAssertions;
using Moq;
using ONEVO.Application.Common.RepositoryInterfaces;
using ONEVO.Application.Features.Auth.Login.RepositoryInterfaces;
using ONEVO.Application.Features.DevPlatform.Tenancy.Commands.RevokeTenantSession;
using ONEVO.Application.Features.DevPlatform.Tenancy.RepositoryInterfaces;
using ONEVO.Domain.Features.Auth.Entities;
using ONEVO.Domain.Features.InfrastructureModule.Entities;
using Xunit;

namespace ONEVO.Tests.Unit.Features.DevPlatform.Tenancy;

public class RevokeTenantSessionCommandHandlerTests
{
    private readonly Mock<ITenantRepository> _tenants = new();
    private readonly Mock<ISessionRepository> _sessions = new();
    private readonly Mock<IUnitOfWork> _uow = new();

    private static readonly Guid TenantId = Guid.NewGuid();
    private static readonly Guid SessionId = Guid.NewGuid();

    private RevokeTenantSessionCommandHandler BuildSut() =>
        new(_tenants.Object, _sessions.Object, _uow.Object);

    [Fact]
    public async Task Handle_UnknownTenant_ReturnsNotFound()
    {
        _tenants.Setup(t => t.GetByIdAsync(TenantId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((Tenant?)null);

        var sut = BuildSut();
        var result = await sut.Handle(new RevokeTenantSessionCommand(TenantId, SessionId), CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.StatusCode.Should().Be(404);
        _sessions.Verify(s => s.RevokeByIdAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Handle_SessionBelongsToDifferentTenant_ReturnsNotFound()
    {
        _tenants.Setup(t => t.GetByIdAsync(TenantId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Tenant { Id = TenantId, Name = "Acme", Slug = "acme" });
        _sessions.Setup(s => s.GetByIdAsync(SessionId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Session { Id = SessionId, TenantId = Guid.NewGuid() });

        var sut = BuildSut();
        var result = await sut.Handle(new RevokeTenantSessionCommand(TenantId, SessionId), CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.StatusCode.Should().Be(404);
        _sessions.Verify(s => s.RevokeByIdAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Handle_ValidSession_RevokesAndPersists()
    {
        _tenants.Setup(t => t.GetByIdAsync(TenantId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Tenant { Id = TenantId, Name = "Acme", Slug = "acme" });
        _sessions.Setup(s => s.GetByIdAsync(SessionId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Session { Id = SessionId, TenantId = TenantId });

        var sut = BuildSut();
        var result = await sut.Handle(new RevokeTenantSessionCommand(TenantId, SessionId), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        _sessions.Verify(s => s.RevokeByIdAsync(SessionId, It.IsAny<CancellationToken>()), Times.Once);
        _uow.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }
}
