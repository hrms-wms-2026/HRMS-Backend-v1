using FluentAssertions;
using Moq;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.Auth.Login.RepositoryInterfaces;
using ONEVO.Application.Features.DevPlatform.Tenancy.Queries.ListTenantSessions;
using ONEVO.Application.Features.DevPlatform.Tenancy.RepositoryInterfaces;
using ONEVO.Domain.Features.Auth.Entities;
using ONEVO.Domain.Features.InfrastructureModule.Entities;
using Xunit;

namespace ONEVO.Tests.Unit.Features.DevPlatform.Tenancy;

public class ListTenantSessionsQueryHandlerTests
{
    private readonly Mock<ITenantRepository> _tenants = new();
    private readonly Mock<ISessionRepository> _sessions = new();
    private readonly Mock<IUserRepository> _users = new();
    private readonly Mock<IDateTimeProvider> _clock = new();

    private static readonly DateTimeOffset Now = new(2026, 8, 17, 12, 0, 0, TimeSpan.Zero);
    private static readonly Guid TenantId = Guid.NewGuid();

    public ListTenantSessionsQueryHandlerTests()
    {
        _clock.SetupGet(c => c.UtcNow).Returns(Now);
    }

    private ListTenantSessionsQueryHandler BuildSut() =>
        new(_tenants.Object, _sessions.Object, _users.Object, _clock.Object);

    [Fact]
    public async Task Handle_UnknownTenant_ReturnsNotFound()
    {
        _tenants.Setup(t => t.GetByIdAsync(TenantId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((Tenant?)null);

        var sut = BuildSut();
        var result = await sut.Handle(new ListTenantSessionsQuery(TenantId), CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.StatusCode.Should().Be(404);
    }

    [Fact]
    public async Task Handle_ListsActiveSessions_WithResolvedUserInfo()
    {
        _tenants.Setup(t => t.GetByIdAsync(TenantId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Tenant { Id = TenantId, Name = "Acme", Slug = "acme" });

        var userId = Guid.NewGuid();
        var session = new Session
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            TenantId = TenantId,
            IpAddress = "10.0.0.1",
            UserAgent = "Chrome/Windows",
            StartedAt = Now.AddHours(-1),
            LastActivityAt = Now.AddMinutes(-5),
            ExpiresAt = Now.AddHours(1),
        };
        _sessions.Setup(s => s.ListActiveByTenantIdAsync(TenantId, Now, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<Session> { session });
        _users.Setup(u => u.GetByIdAsync(userId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new User { Id = userId, Email = "jane@acme.test", FirstName = "Jane", LastName = "Doe" });

        var sut = BuildSut();
        var result = await sut.Handle(new ListTenantSessionsQuery(TenantId), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().ContainSingle();
        var response = result.Value![0];
        response.Id.Should().Be(session.Id);
        response.UserEmail.Should().Be("jane@acme.test");
        response.UserFullName.Should().Be("Jane Doe");
        response.IpAddress.Should().Be("10.0.0.1");
    }
}
