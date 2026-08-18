using FluentAssertions;
using Moq;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.Monitoring.Settings.Commands.UpdateMonitoringFeatureToggles;
using ONEVO.Application.Features.Monitoring.Settings.RepositoryInterfaces;
using ONEVO.Domain.Features.Monitoring.Settings.Entities;
using Xunit;

namespace ONEVO.Tests.Unit.Features.Monitoring.Settings;

public class UpdateMonitoringFeatureTogglesCommandHandlerTests
{
    private readonly Mock<IMonitoringFeatureTogglesRepository> _toggles = new();
    private readonly Mock<ICurrentUser> _currentUser = new();
    private readonly Mock<IDateTimeProvider> _dateTimeProvider = new();
    private readonly Mock<ICacheService> _cache = new();

    private static readonly Guid TenantId = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly DateTimeOffset FixedNow = new(2026, 8, 17, 12, 0, 0, TimeSpan.Zero);

    private UpdateMonitoringFeatureTogglesCommandHandler BuildSut(bool hasPermission = true)
    {
        _currentUser.SetupGet(c => c.IsAuthenticated).Returns(true);
        _currentUser.SetupGet(c => c.TenantId).Returns(TenantId);
        _currentUser.Setup(c => c.HasPermission("monitoring:configure")).Returns(hasPermission);
        _dateTimeProvider.SetupGet(d => d.UtcNow).Returns(FixedNow);
        return new UpdateMonitoringFeatureTogglesCommandHandler(
            _toggles.Object, _currentUser.Object, _dateTimeProvider.Object, _cache.Object);
    }

    private static UpdateMonitoringFeatureTogglesCommand ValidCommand(bool activityMonitoring = true) => new(
        ActivityMonitoring: activityMonitoring,
        ApplicationTracking: true,
        DocumentTracking: false,
        CommunicationTracking: false,
        ScreenshotCapture: true,
        AutoScreenshotCapture: false,
        MeetingDetection: false,
        DeviceTracking: true,
        WorkLocationVerification: false,
        IdentityVerification: false,
        Biometric: false);

    [Fact]
    public async Task Handle_NoExistingRow_CreatesNewRow()
    {
        _toggles.Setup(r => r.GetByTenantIdAsync(TenantId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((MonitoringFeatureToggles?)null);
        MonitoringFeatureToggles? added = null;
        _toggles.Setup(r => r.AddAsync(It.IsAny<MonitoringFeatureToggles>(), It.IsAny<CancellationToken>()))
            .Callback<MonitoringFeatureToggles, CancellationToken>((t, _) => added = t)
            .Returns(Task.CompletedTask);
        var sut = BuildSut();

        var result = await sut.Handle(ValidCommand(), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        added.Should().NotBeNull();
        added!.TenantId.Should().Be(TenantId);
        added.ActivityMonitoring.Should().BeTrue();
        added.CreatedAt.Should().Be(FixedNow);
        added.UpdatedAt.Should().Be(FixedNow);
        _toggles.Verify(r => r.Update(It.IsAny<MonitoringFeatureToggles>()), Times.Never);
        _toggles.Verify(r => r.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Handle_ExistingRow_UpdatesInPlace()
    {
        var existing = new MonitoringFeatureToggles
        {
            Id = Guid.NewGuid(),
            TenantId = TenantId,
            ActivityMonitoring = false,
            CreatedAt = FixedNow.AddDays(-10),
            UpdatedAt = FixedNow.AddDays(-10)
        };
        _toggles.Setup(r => r.GetByTenantIdAsync(TenantId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(existing);
        var sut = BuildSut();

        var result = await sut.Handle(ValidCommand(activityMonitoring: true), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        existing.ActivityMonitoring.Should().BeTrue();
        existing.UpdatedAt.Should().Be(FixedNow);
        _toggles.Verify(r => r.Update(existing), Times.Once);
        _toggles.Verify(r => r.AddAsync(It.IsAny<MonitoringFeatureToggles>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Handle_ValidRequest_InvalidatesTenantToggleCachePrefix()
    {
        _toggles.Setup(r => r.GetByTenantIdAsync(TenantId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((MonitoringFeatureToggles?)null);
        var sut = BuildSut();

        await sut.Handle(ValidCommand(), CancellationToken.None);

        _cache.Verify(c => c.RemoveByPrefixAsync(
            $"tenant:{TenantId}:monitoring-toggle:", It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Handle_Unauthenticated_ReturnsForbidden()
    {
        _currentUser.SetupGet(c => c.IsAuthenticated).Returns(false);
        var sut = new UpdateMonitoringFeatureTogglesCommandHandler(
            _toggles.Object, _currentUser.Object, _dateTimeProvider.Object, _cache.Object);

        var result = await sut.Handle(ValidCommand(), CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.StatusCode.Should().Be(403);
        _toggles.Verify(r => r.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Handle_MissingConfigurePermission_ReturnsForbidden()
    {
        var sut = BuildSut(hasPermission: false);

        var result = await sut.Handle(ValidCommand(), CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.StatusCode.Should().Be(403);
        _toggles.Verify(r => r.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }
}
