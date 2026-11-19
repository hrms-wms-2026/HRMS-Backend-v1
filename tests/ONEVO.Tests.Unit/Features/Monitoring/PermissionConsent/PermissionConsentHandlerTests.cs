using FluentAssertions;
using Moq;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.DevPlatform.Tenancy.RepositoryInterfaces;
using ONEVO.Application.Features.Monitoring.CheckIn.ServiceInterfaces;
using ONEVO.Application.Features.Monitoring.PermissionConsent.Commands.RecordPermissionConsent;
using ONEVO.Application.Features.Monitoring.PermissionConsent.Queries.GetPermissionConsentStatus;
using ONEVO.Application.Features.Monitoring.PermissionConsent.RepositoryInterfaces;
using ONEVO.Domain.Features.InfrastructureModule.Entities;
using ONEVO.Domain.Features.Monitoring.PermissionConsent.Entities;
using Xunit;

namespace ONEVO.Tests.Unit.Features.Monitoring.PermissionConsent;

public class PermissionConsentHandlerTests
{
    private readonly Mock<ITrayCurrentDevice> _device = new();
    private readonly Mock<ITenantRepository> _tenants = new();
    private readonly Mock<ITenantContextSwitcher> _tenantSwitcher = new();
    private readonly Mock<IMonitoringPermissionConsentRepository> _consents = new();
    private readonly Mock<ITrayEmployeeIdentityResolver> _employeeIdentity = new();

    private readonly Guid _tenantId = Guid.NewGuid();
    private readonly Guid _userId = Guid.NewGuid();
    private readonly Guid _employeeId = Guid.NewGuid();
    private readonly Guid _deviceId = Guid.NewGuid();

    public PermissionConsentHandlerTests()
    {
        _device.Setup(d => d.IsAuthenticated).Returns(true);
        _device.Setup(d => d.TenantId).Returns(_tenantId);
        _device.Setup(d => d.UserId).Returns(_userId);
        _device.Setup(d => d.DeviceRegistrationId).Returns(_deviceId);
        _tenants.Setup(t => t.GetByIdAsync(_tenantId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Tenant { Id = _tenantId, Slug = "acme" });
        _tenantSwitcher.Setup(s => s.SwitchToTenantAsync(It.IsAny<TenantRegistryEntry>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        _employeeIdentity.Setup(r => r.ResolveEmployeeIdAsync(
                _tenantId, _userId, It.IsAny<Guid?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(_employeeId);
    }

    private GetPermissionConsentStatusQueryHandler CreateQuery() => new(
        _device.Object, _tenants.Object, _tenantSwitcher.Object, _consents.Object, _employeeIdentity.Object);

    private RecordPermissionConsentCommandHandler CreateCommand() => new(
        _device.Object, _tenants.Object, _tenantSwitcher.Object, _consents.Object, _employeeIdentity.Object);

    [Fact]
    public async Task Status_NeverAccepted_NotAccepted()
    {
        _consents.Setup(c => c.GetLatestAsync(_tenantId, _employeeId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((MonitoringPermissionConsent?)null);

        var result = await CreateQuery().Handle(new GetPermissionConsentStatusQuery(), CancellationToken.None);

        result.Value!.Accepted.Should().BeFalse();
        result.Value.AcceptedAt.Should().BeNull();
    }

    [Fact]
    public async Task Status_Accepted_ReturnsLatestToggles()
    {
        var acceptedAt = DateTimeOffset.UtcNow.AddDays(-3);
        _consents.Setup(c => c.GetLatestAsync(_tenantId, _employeeId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new MonitoringPermissionConsent
            {
                TenantId = _tenantId, EmployeeId = _employeeId, AcceptedAt = acceptedAt,
                ScreenMonitoring = true, AppTracking = true, CameraAccess = false
            });

        var result = await CreateQuery().Handle(new GetPermissionConsentStatusQuery(), CancellationToken.None);

        result.Value!.Accepted.Should().BeTrue();
        result.Value.AcceptedAt.Should().Be(acceptedAt);
        result.Value.AppTracking.Should().BeTrue();
        result.Value.CameraAccess.Should().BeFalse();
    }

    [Fact]
    public async Task Record_SavesRowForEmployeeAndDevice_AfterTenantSwitch()
    {
        var order = new List<string>();
        _tenantSwitcher.Setup(s => s.SwitchToTenantAsync(It.IsAny<TenantRegistryEntry>(), It.IsAny<CancellationToken>()))
            .Callback(() => order.Add("switch"))
            .Returns(Task.CompletedTask);
        MonitoringPermissionConsent? saved = null;
        _consents.Setup(c => c.AddAsync(It.IsAny<MonitoringPermissionConsent>(), It.IsAny<CancellationToken>()))
            .Callback<MonitoringPermissionConsent, CancellationToken>((c, _) => { saved = c; order.Add("add"); })
            .Returns(Task.CompletedTask);

        var result = await CreateCommand().Handle(
            new RecordPermissionConsentCommand(true, true, true, true, false, true), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value!.Accepted.Should().BeTrue();
        saved.Should().NotBeNull();
        saved!.TenantId.Should().Be(_tenantId);
        saved.EmployeeId.Should().Be(_employeeId);
        saved.DeviceRegistrationId.Should().Be(_deviceId);
        saved.CameraAccess.Should().BeTrue();
        saved.Notifications.Should().BeFalse();
        order.Should().Equal("switch", "add");
        _consents.Verify(c => c.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Unauthenticated_Returns401()
    {
        _device.Setup(d => d.IsAuthenticated).Returns(false);

        var status = await CreateQuery().Handle(new GetPermissionConsentStatusQuery(), CancellationToken.None);
        var record = await CreateCommand().Handle(
            new RecordPermissionConsentCommand(true, true, true, true, true, true), CancellationToken.None);

        status.StatusCode.Should().Be(401);
        record.StatusCode.Should().Be(401);
        _consents.Verify(c => c.AddAsync(It.IsAny<MonitoringPermissionConsent>(), It.IsAny<CancellationToken>()), Times.Never);
    }
}
