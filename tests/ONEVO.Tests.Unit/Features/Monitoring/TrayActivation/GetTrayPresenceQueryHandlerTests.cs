using Microsoft.Extensions.Options;
using Moq;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.Monitoring.TrayActivation.Options;
using ONEVO.Application.Features.Monitoring.TrayActivation.Queries.GetTrayPresence;
using ONEVO.Application.Features.Monitoring.TrayActivation.RepositoryInterfaces;
using ONEVO.Application.Features.Monitoring.TrayActivation.ServiceInterfaces;
using ONEVO.Domain.Features.Monitoring.TrayActivation.Entities;

namespace ONEVO.Tests.Unit.Features.Monitoring.TrayActivation;

public sealed class GetTrayPresenceQueryHandlerTests
{
    private readonly Guid _tenantId = Guid.NewGuid();
    private readonly Guid _userId = Guid.NewGuid();
    private readonly DateTimeOffset _now = DateTimeOffset.UtcNow;

    private GetTrayPresenceQueryHandler Build(string mode, bool userRequiresTray, TrayDeviceRegistration? device = null)
    {
        var repository = new Mock<ITrayActivationRepository>();
        repository
            .Setup(r => r.FindLatestActiveDeviceForUserAsync(_userId, _tenantId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(device);

        var user = new Mock<ICurrentUser>();
        user.Setup(u => u.UserId).Returns(_userId);
        user.Setup(u => u.TenantId).Returns(_tenantId);

        var clock = new Mock<IDateTimeProvider>();
        clock.Setup(c => c.UtcNow).Returns(_now);

        var requirement = new Mock<ITrayPresenceRequirementEvaluator>();
        requirement
            .Setup(r => r.IsRequiredForCurrentUserAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(userRequiresTray);

        return new GetTrayPresenceQueryHandler(
            repository.Object,
            user.Object,
            clock.Object,
            Options.Create(new TrayPresenceOptions { Mode = mode, GracePeriodSeconds = 120 }),
            requirement.Object);
    }

    [Fact]
    public async Task Required_WhenEnforcingAndTheUserMustRunTheTray()
    {
        var result = await Build("Enforce", userRequiresTray: true)
            .Handle(new GetTrayPresenceQuery(), CancellationToken.None);

        Assert.True(result.Value!.Required);
        Assert.False(result.Value.Connected);
    }

    [Fact]
    public async Task NotRequired_WhenEnforcingButTheUserIsNotMonitored()
    {
        var result = await Build("Enforce", userRequiresTray: false)
            .Handle(new GetTrayPresenceQuery(), CancellationToken.None);

        Assert.False(result.Value!.Required);
    }

    [Theory]
    [InlineData("Observe")]
    [InlineData("Off")]
    public async Task NotRequired_WhenModeIsNotEnforce_EvenForAMonitoredUser(string mode)
    {
        var result = await Build(mode, userRequiresTray: true)
            .Handle(new GetTrayPresenceQuery(), CancellationToken.None);

        Assert.False(result.Value!.Required);
    }

    [Fact]
    public async Task Connected_WhenTheTrayHeartbeatIsInsideTheGracePeriod()
    {
        var device = new TrayDeviceRegistration
        {
            Id = Guid.NewGuid(), TenantId = _tenantId, UserId = _userId,
            DeviceName = "PC", LastSeenAt = _now.AddSeconds(-10)
        };

        var result = await Build("Enforce", userRequiresTray: true, device)
            .Handle(new GetTrayPresenceQuery(), CancellationToken.None);

        Assert.True(result.Value!.Required);
        Assert.True(result.Value.Connected);
        Assert.Equal("PC", result.Value.Device!.Name);
    }
}
