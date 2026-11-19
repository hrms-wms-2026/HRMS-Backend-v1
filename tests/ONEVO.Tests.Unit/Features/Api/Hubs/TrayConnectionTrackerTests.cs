using FluentAssertions;
using ONEVO.Api.Hubs;
using Xunit;

namespace ONEVO.Tests.Unit.Features.Api.Hubs;

public class TrayConnectionTrackerTests
{
    private readonly Guid _tenantId = Guid.NewGuid();
    private readonly Guid _userId = Guid.NewGuid();

    [Fact]
    public void NoConnection_IsNotConnected()
    {
        new TrayConnectionTracker().IsConnected(_tenantId, _userId).Should().BeFalse();
    }

    [Fact]
    public void ReconnectOverlap_StaysConnectedUntilLastConnectionCloses()
    {
        var sut = new TrayConnectionTracker();

        sut.Connected(_tenantId, _userId);
        sut.Connected(_tenantId, _userId); // new connection up before the old one's disconnect
        sut.Disconnected(_tenantId, _userId);

        sut.IsConnected(_tenantId, _userId).Should().BeTrue();

        sut.Disconnected(_tenantId, _userId);
        sut.IsConnected(_tenantId, _userId).Should().BeFalse();
    }

    [Fact]
    public void Connections_AreScopedPerTenantAndUser()
    {
        var sut = new TrayConnectionTracker();
        sut.Connected(_tenantId, _userId);

        sut.IsConnected(Guid.NewGuid(), _userId).Should().BeFalse();
        sut.IsConnected(_tenantId, Guid.NewGuid()).Should().BeFalse();
    }

    [Fact]
    public void ExtraDisconnect_NeverGoesNegative()
    {
        var sut = new TrayConnectionTracker();
        sut.Disconnected(_tenantId, _userId);
        sut.Connected(_tenantId, _userId);

        sut.IsConnected(_tenantId, _userId).Should().BeTrue();
    }
}
