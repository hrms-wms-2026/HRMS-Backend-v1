using FluentAssertions;
using Moq;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.Monitoring.Settings.Queries.GetMonitoringFeatureToggles;
using ONEVO.Application.Features.Monitoring.Settings.RepositoryInterfaces;
using ONEVO.Domain.Features.Monitoring.Settings.Entities;
using Xunit;

namespace ONEVO.Tests.Unit.Features.Monitoring.Settings;

public class GetMonitoringFeatureTogglesQueryHandlerTests
{
    private readonly Mock<IMonitoringFeatureTogglesRepository> _toggles = new();
    private readonly Mock<ICurrentUser> _currentUser = new();

    private static readonly Guid TenantId = Guid.Parse("11111111-1111-1111-1111-111111111111");

    private GetMonitoringFeatureTogglesQueryHandler BuildSut()
    {
        _currentUser.SetupGet(c => c.IsAuthenticated).Returns(true);
        _currentUser.SetupGet(c => c.TenantId).Returns(TenantId);
        return new GetMonitoringFeatureTogglesQueryHandler(_toggles.Object, _currentUser.Object);
    }

    [Fact]
    public async Task Handle_NoExistingRow_ReturnsAllFalseDefaults()
    {
        _toggles.Setup(r => r.GetByTenantIdAsync(TenantId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((MonitoringFeatureToggles?)null);
        var sut = BuildSut();

        var result = await sut.Handle(new GetMonitoringFeatureTogglesQuery(), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value!.ActivityMonitoring.Should().BeFalse();
        result.Value.Biometric.Should().BeFalse();
        result.Value.UpdatedAt.Should().BeNull();
    }

    [Fact]
    public async Task Handle_ExistingRow_ReturnsMappedValues()
    {
        var updatedAt = new DateTimeOffset(2026, 8, 17, 10, 0, 0, TimeSpan.Zero);
        _toggles.Setup(r => r.GetByTenantIdAsync(TenantId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new MonitoringFeatureToggles
            {
                Id = Guid.NewGuid(),
                TenantId = TenantId,
                ActivityMonitoring = true,
                ScreenshotCapture = true,
                UpdatedAt = updatedAt
            });
        var sut = BuildSut();

        var result = await sut.Handle(new GetMonitoringFeatureTogglesQuery(), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value!.ActivityMonitoring.Should().BeTrue();
        result.Value.ScreenshotCapture.Should().BeTrue();
        result.Value.ApplicationTracking.Should().BeFalse();
        result.Value.UpdatedAt.Should().Be(updatedAt);
    }

    [Fact]
    public async Task Handle_Unauthenticated_ReturnsForbidden()
    {
        _currentUser.SetupGet(c => c.IsAuthenticated).Returns(false);
        var sut = new GetMonitoringFeatureTogglesQueryHandler(_toggles.Object, _currentUser.Object);

        var result = await sut.Handle(new GetMonitoringFeatureTogglesQuery(), CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.StatusCode.Should().Be(403);
    }
}
