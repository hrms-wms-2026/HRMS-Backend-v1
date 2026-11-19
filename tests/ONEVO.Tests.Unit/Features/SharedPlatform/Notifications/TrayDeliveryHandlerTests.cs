using FluentAssertions;
using Moq;
using ONEVO.Application.Common.RepositoryInterfaces;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.DevPlatform.Tenancy.RepositoryInterfaces;
using ONEVO.Application.Features.Monitoring.CheckIn.ServiceInterfaces;
using ONEVO.Application.Features.SharedPlatform.Notifications.Tray.Commands.AckTrayDelivery;
using ONEVO.Application.Features.SharedPlatform.Notifications.Tray.Queries.GetPendingTrayDeliveries;
using ONEVO.Domain.Features.InfrastructureModule.Entities;
using ONEVO.Domain.Features.SharedPlatform.Notifications.Entities;
using Xunit;

namespace ONEVO.Tests.Unit.Features.SharedPlatform.Notifications;

public class TrayDeliveryHandlerTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 18, 14, 0, 0, TimeSpan.Zero);

    private readonly NotificationTestFakes.FakeDeliveryRepository _deliveries = new();
    private readonly Mock<ITrayCurrentDevice> _device = new();
    private readonly Mock<ITenantRepository> _tenants = new();
    private readonly Mock<ITenantContextSwitcher> _switcher = new();
    private readonly Mock<IUnitOfWork> _uow = new();
    private readonly Mock<IDateTimeProvider> _clock = new();
    private readonly Guid _tenant = Guid.NewGuid();
    private readonly Guid _user = Guid.NewGuid();

    public TrayDeliveryHandlerTests()
    {
        _clock.SetupGet(c => c.UtcNow).Returns(Now);
        _device.SetupGet(d => d.IsAuthenticated).Returns(true);
        _device.SetupGet(d => d.TenantId).Returns(_tenant);
        _device.SetupGet(d => d.UserId).Returns(_user);
        _tenants.Setup(t => t.GetByIdAsync(_tenant, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Tenant { Id = _tenant, Slug = "acme", Status = TenantStatus.Active });
    }

    private NotificationDelivery Tray(Guid? tenant = null, Guid? user = null, DateTimeOffset? expiresAt = null,
        NotificationChannels channel = NotificationChannels.Tray, string status = NotificationDeliveryStatus.Pending)
    {
        var d = new NotificationDelivery
        {
            Id = Guid.NewGuid(), TenantId = tenant ?? _tenant, EventId = Guid.NewGuid(), EventKey = "work_approval_requested",
            RecipientUserId = user ?? _user, Channel = channel, Title = "Approval needed", Body = "Bala asked",
            ActionKey = "open_related", RelatedEntityType = "work_approval_request", RelatedEntityId = Guid.NewGuid(),
            Status = status, CreatedAt = Now.AddMinutes(-5), ExpiresAt = expiresAt ?? Now.AddHours(1)
        };
        _deliveries.Deliveries.Add(d);
        return d;
    }

    private GetPendingTrayDeliveriesQueryHandler Pending() => new(_deliveries, _device.Object, _tenants.Object, _switcher.Object, _clock.Object);
    private AckTrayDeliveryCommandHandler Ack() => new(_deliveries, _device.Object, _tenants.Object, _switcher.Object, _uow.Object, _clock.Object);

    [Fact]
    public async Task Pending_ReturnsOnlyThisDeviceUsersUnexpiredTrayDeliveries_AfterSwitchingToTheDeviceTenant()
    {
        var mine = Tray();
        Tray(user: Guid.NewGuid());
        Tray(tenant: Guid.NewGuid());
        Tray(expiresAt: Now.AddMinutes(-1));
        Tray(channel: NotificationChannels.InApp);
        Tray(status: NotificationDeliveryStatus.ClientAccepted);

        var result = await Pending().Handle(new GetPendingTrayDeliveriesQuery(), default);

        var item = result.Value!.Should().ContainSingle().Subject;
        item.DeliveryId.Should().Be(mine.Id);
        item.Title.Should().Be("Approval needed");
        item.ActionKey.Should().Be("open_related");
        _switcher.Verify(s => s.SwitchToTenantAsync(It.Is<TenantRegistryEntry>(t => t.TenantId == _tenant), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Pending_WithoutAValidDevice_Is401()
    {
        _device.SetupGet(d => d.IsAuthenticated).Returns(false);

        (await Pending().Handle(new GetPendingTrayDeliveriesQuery(), default)).StatusCode.Should().Be(401);
    }

    [Fact]
    public async Task Pending_UnknownTenant_Is401()
    {
        _tenants.Setup(t => t.GetByIdAsync(_tenant, It.IsAny<CancellationToken>())).ReturnsAsync((Tenant?)null);

        (await Pending().Handle(new GetPendingTrayDeliveriesQuery(), default)).StatusCode.Should().Be(401);
    }

    [Fact]
    public async Task Ack_MarksClientAccepted_AndIsIdempotent()
    {
        var d = Tray();

        var first = await Ack().Handle(new AckTrayDeliveryCommand(d.Id), default);
        var second = await Ack().Handle(new AckTrayDeliveryCommand(d.Id), default);

        first.IsSuccess.Should().BeTrue();
        second.IsSuccess.Should().BeTrue();
        d.Status.Should().Be(NotificationDeliveryStatus.ClientAccepted);
        d.CompletedAt.Should().Be(Now);
    }

    [Fact]
    public async Task Ack_AnotherUsersDelivery_IsNotFound()
    {
        var other = Tray(user: Guid.NewGuid());

        var result = await Ack().Handle(new AckTrayDeliveryCommand(other.Id), default);

        result.StatusCode.Should().Be(404);
        other.Status.Should().Be(NotificationDeliveryStatus.Pending);
    }

    [Fact]
    public async Task Ack_NonTrayDelivery_IsNotFound()
    {
        var inApp = Tray(channel: NotificationChannels.InApp);

        (await Ack().Handle(new AckTrayDeliveryCommand(inApp.Id), default)).StatusCode.Should().Be(404);
    }

    [Fact]
    public async Task Ack_AfterExpiry_IsRecordedAsExpired_NotAccepted()
    {
        var late = Tray(expiresAt: Now.AddMinutes(-1));

        var result = await Ack().Handle(new AckTrayDeliveryCommand(late.Id), default);

        result.StatusCode.Should().Be(409);
        late.Status.Should().Be(NotificationDeliveryStatus.Expired);
    }
}
