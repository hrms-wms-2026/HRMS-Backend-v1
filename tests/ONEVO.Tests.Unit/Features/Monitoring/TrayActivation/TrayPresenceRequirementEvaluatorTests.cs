using Moq;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.Monitoring.ActivityMonitoring.ServiceInterfaces;
using ONEVO.Application.Features.Monitoring.TrayActivation.Services;

namespace ONEVO.Tests.Unit.Features.Monitoring.TrayActivation;

public sealed class TrayPresenceRequirementEvaluatorTests
{
    private readonly Guid _tenantId = Guid.NewGuid();
    private readonly Guid _userId = Guid.NewGuid();
    private readonly Mock<ICurrentUser> _user = new();
    private readonly Mock<IModuleEntitlementService> _entitlements = new();
    private readonly Mock<IMonitoringToggleResolver> _toggles = new();

    private TrayPresenceRequirementEvaluator Build(bool moduleEnabled, Guid? legalEntityId = null, bool authenticated = true)
    {
        _user.Setup(u => u.IsAuthenticated).Returns(authenticated);
        _user.Setup(u => u.TenantId).Returns(_tenantId);
        _user.Setup(u => u.UserId).Returns(_userId);
        _user.Setup(u => u.LegalEntityId).Returns(legalEntityId);
        _entitlements
            .Setup(e => e.IsModuleEnabledAsync(_tenantId, TrayPresenceRequirementEvaluator.MonitoringModuleKey, It.IsAny<CancellationToken>()))
            .ReturnsAsync(moduleEnabled);
        _toggles
            .Setup(t => t.IsEnabledAsync(It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<MonitoringCapability>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);
        _toggles
            .Setup(t => t.IsEnabledAsync(It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<MonitoringCapability>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);
        _toggles
            .Setup(t => t.IsPhotoRequiredAsync(It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);
        _toggles
            .Setup(t => t.IsPhotoRequiredAsync(It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);
        return new TrayPresenceRequirementEvaluator(_user.Object, _entitlements.Object, _toggles.Object);
    }

    [Fact]
    public async Task NotRequired_WhenTenantLacksTheMonitoringModule()
    {
        var sut = Build(moduleEnabled: false);
        // Even a fully-enabled user is not gated when the tenant never bought monitoring.
        _toggles
            .Setup(t => t.IsEnabledAsync(It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<MonitoringCapability>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        Assert.False(await sut.IsRequiredForCurrentUserAsync(CancellationToken.None));
    }

    [Fact]
    public async Task NotRequired_WhenNoCapabilityIsEnabledForTheUser()
    {
        var sut = Build(moduleEnabled: true);

        Assert.False(await sut.IsRequiredForCurrentUserAsync(CancellationToken.None));
    }

    [Theory]
    [InlineData(MonitoringCapability.ActivityMonitoring)]
    [InlineData(MonitoringCapability.ApplicationTracking)]
    [InlineData(MonitoringCapability.ScreenshotCapture)]
    [InlineData(MonitoringCapability.WorkLocationVerification)]
    public async Task Required_WhenAnyMonitoringCapabilityIsEnabled(MonitoringCapability enabledCapability)
    {
        var sut = Build(moduleEnabled: true);
        _toggles
            .Setup(t => t.IsEnabledAsync(_tenantId, _userId, enabledCapability, It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        Assert.True(await sut.IsRequiredForCurrentUserAsync(CancellationToken.None));
    }

    [Theory]
    [InlineData(MonitoringCapability.AutoScreenshotCapture)]
    [InlineData(MonitoringCapability.DeviceTracking)]
    [InlineData(MonitoringCapability.IdentityVerification)]
    [InlineData(MonitoringCapability.DocumentTracking)]
    [InlineData(MonitoringCapability.CommunicationTracking)]
    [InlineData(MonitoringCapability.MeetingDetection)]
    [InlineData(MonitoringCapability.Biometric)]
    public async Task NotRequired_WhenOnlyANonGatingCapabilityIsEnabled(MonitoringCapability hiddenCapability)
    {
        // These capabilities have no checkbox on the Monitoring Configuration page - an admin
        // can never turn them off, so they must never independently force the tray requirement
        // (e.g. a stale seed default sitting at true forever).
        var sut = Build(moduleEnabled: true);
        _toggles
            .Setup(t => t.IsEnabledAsync(_tenantId, _userId, hiddenCapability, It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        Assert.False(await sut.IsRequiredForCurrentUserAsync(CancellationToken.None));
    }

    [Fact]
    public async Task Required_WhenWorkModePhotoIsRequired_EvenIfNoCapabilityToggleIsEnabled()
    {
        var sut = Build(moduleEnabled: true);
        _toggles
            .Setup(t => t.IsPhotoRequiredAsync(_tenantId, _userId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        Assert.True(await sut.IsRequiredForCurrentUserAsync(CancellationToken.None));
    }

    [Fact]
    public async Task UsesTheLegalEntityOverload_WhenTheUserHasAnActiveCompany()
    {
        var legalEntityId = Guid.NewGuid();
        var sut = Build(moduleEnabled: true, legalEntityId);
        _toggles
            .Setup(t => t.IsEnabledAsync(_tenantId, _userId, legalEntityId, MonitoringCapability.ApplicationTracking, It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        Assert.True(await sut.IsRequiredForCurrentUserAsync(CancellationToken.None));
        _toggles.Verify(t => t.IsEnabledAsync(It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<MonitoringCapability>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task NotRequired_ForUnauthenticatedCaller_WithoutTouchingAnyService()
    {
        var sut = Build(moduleEnabled: true, authenticated: false);

        Assert.False(await sut.IsRequiredForCurrentUserAsync(CancellationToken.None));
        _entitlements.Verify(e => e.IsModuleEnabledAsync(It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }
}
