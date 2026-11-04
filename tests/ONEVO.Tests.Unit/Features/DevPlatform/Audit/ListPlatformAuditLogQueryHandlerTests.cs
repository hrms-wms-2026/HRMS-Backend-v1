using FluentAssertions;
using Moq;
using ONEVO.Application.Features.DevPlatform.Audit.Queries.ListPlatformAuditLog;
using ONEVO.Application.Features.DevPlatform.Audit.RepositoryInterfaces;
using ONEVO.Application.Features.DevPlatform.PlatformAccess.RepositoryInterfaces;
using ONEVO.Domain.Features.DevPlatform.Audit.Entities;
using ONEVO.Domain.Features.DevPlatform.PlatformAccess.Entities;
using Xunit;

namespace ONEVO.Tests.Unit.Features.DevPlatform.Audit;

public class ListPlatformAuditLogQueryHandlerTests
{
    private readonly Mock<IPlatformAuditLogRepository> _auditLogs = new();
    private readonly Mock<IPlatformUserRepository> _users = new();

    private ListPlatformAuditLogQueryHandler Sut() => new(_auditLogs.Object, _users.Object);

    [Fact]
    public async Task Handle_MapsRowsWithActorEmailAndParsedJson()
    {
        var actorId = Guid.NewGuid();
        var row = new PlatformAuditLog
        {
            Id = Guid.NewGuid(),
            ActorPlatformUserId = actorId,
            Action = "tenant.created",
            TargetType = "tenant",
            TargetId = "t-1",
            BeforeJson = null,
            AfterJson = "{\"slug\":\"acme\"}",
            IpAddress = "203.0.113.7",
            CreatedAt = DateTimeOffset.UtcNow,
        };
        _auditLogs.Setup(r => r.ListAsync(It.IsAny<PlatformAuditLogFilter>(), 1, 25, It.IsAny<CancellationToken>()))
            .ReturnsAsync((new[] { row }, 1));
        _users.Setup(u => u.GetByIdAsync(actorId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PlatformUser { Id = actorId, Email = "admin@onevo.test" });

        var result = await Sut().Handle(new ListPlatformAuditLogQuery(Page: 1, PageSize: 25), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value!.TotalCount.Should().Be(1);
        var item = result.Value.Items.Should().ContainSingle().Subject;
        item.ActorEmail.Should().Be("admin@onevo.test");
        item.Action.Should().Be("tenant.created");
        item.Before.Should().BeNull();
        item.After!.Value.GetProperty("slug").GetString().Should().Be("acme");
    }

    [Theory]
    [InlineData(0, 25, 1, 25)]
    [InlineData(2, 0, 2, 1)]
    [InlineData(1, 500, 1, 100)]
    public async Task Handle_ClampsPaging(int page, int pageSize, int expectedPage, int expectedPageSize)
    {
        _auditLogs.Setup(r => r.ListAsync(It.IsAny<PlatformAuditLogFilter>(), expectedPage, expectedPageSize, It.IsAny<CancellationToken>()))
            .ReturnsAsync((Array.Empty<PlatformAuditLog>(), 0));

        var result = await Sut().Handle(new ListPlatformAuditLogQuery(Page: page, PageSize: pageSize), CancellationToken.None);

        result.Value!.Page.Should().Be(expectedPage);
        result.Value.PageSize.Should().Be(expectedPageSize);
    }

    [Fact]
    public async Task Handle_PassesFiltersToTheRepository()
    {
        var tenantId = Guid.NewGuid();
        PlatformAuditLogFilter? captured = null;
        _auditLogs.Setup(r => r.ListAsync(It.IsAny<PlatformAuditLogFilter>(), It.IsAny<int>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .Callback<PlatformAuditLogFilter, int, int, CancellationToken>((f, _, _, _) => captured = f)
            .ReturnsAsync((Array.Empty<PlatformAuditLog>(), 0));

        await Sut().Handle(new ListPlatformAuditLogQuery(Action: "tenant.created", TargetTenantId: tenantId), CancellationToken.None);

        captured!.Action.Should().Be("tenant.created");
        captured.TargetTenantId.Should().Be(tenantId);
    }

    [Fact]
    public async Task Handle_FromAfterTo_IsRejected()
    {
        var now = DateTimeOffset.UtcNow;

        var result = await Sut().Handle(new ListPlatformAuditLogQuery(From: now, To: now.AddDays(-1)), CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.StatusCode.Should().Be(400);
    }
}
