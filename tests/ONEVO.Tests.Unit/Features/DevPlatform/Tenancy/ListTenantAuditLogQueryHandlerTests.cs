using FluentAssertions;
using Moq;
using ONEVO.Application.Features.Auth.Login.RepositoryInterfaces;
using ONEVO.Application.Features.DevPlatform.Tenancy.Queries.ListTenantAuditLog;
using ONEVO.Application.Features.DevPlatform.Tenancy.RepositoryInterfaces;
using ONEVO.Domain.Features.Auth.Entities;
using ONEVO.Domain.Features.InfrastructureModule.Entities;
using Xunit;

namespace ONEVO.Tests.Unit.Features.DevPlatform.Tenancy;

public class ListTenantAuditLogQueryHandlerTests
{
    private readonly Mock<ITenantRepository> _tenants = new();
    private readonly Mock<IAuditLogRepository> _auditLogs = new();
    private readonly Mock<IUserRepository> _users = new();

    private static readonly Guid TenantId = Guid.NewGuid();

    private ListTenantAuditLogQueryHandler BuildSut() =>
        new(_tenants.Object, _auditLogs.Object, _users.Object);

    [Fact]
    public async Task Handle_UnknownTenant_ReturnsNotFound()
    {
        _tenants.Setup(t => t.GetByIdAsync(TenantId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((Tenant?)null);

        var sut = BuildSut();
        var result = await sut.Handle(new ListTenantAuditLogQuery(TenantId, 1, 25), CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.StatusCode.Should().Be(404);
    }

    [Fact]
    public async Task Handle_ReturnsPagedEntries_WithResolvedUserEmail()
    {
        _tenants.Setup(t => t.GetByIdAsync(TenantId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Tenant { Id = TenantId, Name = "Acme", Slug = "acme" });

        var userId = Guid.NewGuid();
        var entry = new AuditLog
        {
            Id = Guid.NewGuid(),
            TenantId = TenantId,
            UserId = userId,
            Action = "tenant.suspended",
            ResourceType = "tenant",
            ResourceId = TenantId,
            IpAddress = "10.0.0.1",
            CreatedAt = new DateTimeOffset(2026, 8, 17, 10, 0, 0, TimeSpan.Zero),
        };
        _auditLogs.Setup(a => a.ListByTenantIdAsync(TenantId, 1, 25, It.IsAny<CancellationToken>()))
            .ReturnsAsync((new List<AuditLog> { entry }, 1));
        _users.Setup(u => u.GetByIdAsync(userId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new User { Id = userId, Email = "jane@acme.test" });

        var sut = BuildSut();
        var result = await sut.Handle(new ListTenantAuditLogQuery(TenantId, 1, 25), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value!.TotalCount.Should().Be(1);
        result.Value.Items.Should().ContainSingle();
        result.Value.Items[0].UserEmail.Should().Be("jane@acme.test");
        result.Value.Items[0].Action.Should().Be("tenant.suspended");
    }

    [Fact]
    public async Task Handle_EntryWithoutUserId_LeavesUserEmailNull()
    {
        _tenants.Setup(t => t.GetByIdAsync(TenantId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Tenant { Id = TenantId, Name = "Acme", Slug = "acme" });

        var entry = new AuditLog
        {
            Id = Guid.NewGuid(),
            TenantId = TenantId,
            UserId = null,
            Action = "system.migration",
            ResourceType = "tenant",
            CreatedAt = DateTimeOffset.UtcNow,
        };
        _auditLogs.Setup(a => a.ListByTenantIdAsync(TenantId, 1, 25, It.IsAny<CancellationToken>()))
            .ReturnsAsync((new List<AuditLog> { entry }, 1));

        var sut = BuildSut();
        var result = await sut.Handle(new ListTenantAuditLogQuery(TenantId, 1, 25), CancellationToken.None);

        result.Value!.Items[0].UserEmail.Should().BeNull();
        _users.Verify(u => u.GetByIdAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()), Times.Never);
    }
}
