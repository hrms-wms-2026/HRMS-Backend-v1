using System.Security.Claims;
using FluentAssertions;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using ONEVO.Api.Hubs;
using ONEVO.Application.Features.Monitoring.TrayActivation.RepositoryInterfaces;
using ONEVO.Domain.Features.Monitoring.TrayActivation.Entities;
using Xunit;

namespace ONEVO.Tests.Unit.Features.Api.Hubs;

public class AgentCommandsHubTests
{
    private readonly Guid _tenantId = Guid.NewGuid();
    private readonly Guid _userId = Guid.NewGuid();
    private readonly Guid _deviceId = Guid.NewGuid();
    private readonly TrayConnectionTracker _tracker = new();
    private readonly Mock<ITrayActivationRepository> _devices = new();
    private readonly Mock<HubCallerContext> _context = new();
    private readonly Mock<ISingleClientProxy> _caller = new();

    private AgentCommandsHub CreateHub()
    {
        var principal = new ClaimsPrincipal(new ClaimsIdentity(
        [
            new Claim(ClaimTypes.NameIdentifier, _deviceId.ToString()),
            new Claim("user_id", _userId.ToString()),
            new Claim("tenant_id", _tenantId.ToString()),
        ], "Test"));
        _context.Setup(c => c.User).Returns(principal);
        _context.Setup(c => c.ConnectionId).Returns("conn-1");
        _context.Setup(c => c.Items).Returns(new Dictionary<object, object?>());

        var clients = new Mock<IHubCallerClients>();
        clients.Setup(c => c.Caller).Returns(_caller.Object);

        return new AgentCommandsHub(_tracker, _devices.Object, NullLogger<AgentCommandsHub>.Instance)
        {
            Context = _context.Object,
            Clients = clients.Object,
            Groups = Mock.Of<IGroupManager>(),
        };
    }

    [Fact]
    public async Task ActiveDevice_IsTrackedAsPresent_AndToldPushIsReady()
    {
        _devices.Setup(d => d.FindActiveDeviceAsync(_deviceId, _tenantId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new TrayDeviceRegistration { Id = _deviceId, TenantId = _tenantId, UserId = _userId });

        await CreateHub().OnConnectedAsync();

        _tracker.IsConnected(_tenantId, _userId).Should().BeTrue();
        _caller.Verify(c => c.SendCoreAsync("PushReady", It.IsAny<object?[]>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task InactiveDevice_IsAbortedAndNotCountedAsPresent()
    {
        _devices.Setup(d => d.FindActiveDeviceAsync(_deviceId, _tenantId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((TrayDeviceRegistration?)null);

        await CreateHub().OnConnectedAsync();

        _tracker.IsConnected(_tenantId, _userId).Should().BeFalse();
        _context.Verify(c => c.Abort(), Times.Once);
    }

    [Fact]
    public async Task DeviceOfAnotherUser_IsAbortedAndNotCountedAsPresent()
    {
        _devices.Setup(d => d.FindActiveDeviceAsync(_deviceId, _tenantId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new TrayDeviceRegistration { Id = _deviceId, TenantId = _tenantId, UserId = Guid.NewGuid() });

        await CreateHub().OnConnectedAsync();

        _tracker.IsConnected(_tenantId, _userId).Should().BeFalse();
        _context.Verify(c => c.Abort(), Times.Once);
    }

    [Fact]
    public async Task RefusedConnection_Disconnecting_DoesNotUnderflowAnotherConnection()
    {
        _tracker.Connected(_tenantId, _userId); // a legitimate connection already open
        _devices.Setup(d => d.FindActiveDeviceAsync(_deviceId, _tenantId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((TrayDeviceRegistration?)null);
        var hub = CreateHub();

        await hub.OnConnectedAsync();
        await hub.OnDisconnectedAsync(null);

        _tracker.IsConnected(_tenantId, _userId).Should().BeTrue();
    }
}
