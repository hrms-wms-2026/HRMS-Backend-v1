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
    private static readonly Guid LegalEntityId = Guid.Parse("22222222-2222-2222-2222-222222222222");

    private GetMonitoringFeatureTogglesQueryHandler BuildSut()
    {
        _currentUser.SetupGet(c => c.IsAuthenticated).Returns(true);
        _currentUser.SetupGet(c => c.TenantId).Returns(TenantId);
        _currentUser.SetupGet(c => c.LegalEntityId).Returns(LegalEntityId);
        _toggles.Setup(r => r.LegalEntityExistsAsync(TenantId, LegalEntityId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);
        return new GetMonitoringFeatureTogglesQueryHandler(_toggles.Object, _currentUser.Object);
    }

    [Fact]
    public async Task Handle_NoExistingRow_ReturnsAllFalseDefaults()
    {
        _toggles.Setup(r => r.GetByLegalEntityIdAsync(TenantId, LegalEntityId, true, It.IsAny<CancellationToken>()))
            .ReturnsAsync((MonitoringFeatureToggles?)null);
        var sut = BuildSut();

        var result = await sut.Handle(new GetMonitoringFeatureTogglesQuery(), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value!.ActivityMonitoring.Should().BeFalse();
        result.Value.Biometric.Should().BeFalse();
        result.Value.IdleThresholdMinutes.Should().Be(2); // default when no row exists
        result.Value.UpdatedAt.Should().BeNull();
    }

    [Fact]
    public async Task Handle_ExistingRow_ReturnsMappedValues()
    {
        var updatedAt = new DateTimeOffset(2026, 8, 17, 10, 0, 0, TimeSpan.Zero);
        _toggles.Setup(r => r.GetByLegalEntityIdAsync(TenantId, LegalEntityId, true, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new MonitoringFeatureToggles
            {
                Id = Guid.NewGuid(),
                TenantId = TenantId,
                ActivityMonitoring = true,
                ScreenshotCapture = true,
                IdleThresholdMinutes = 12,
                UpdatedAt = updatedAt
            });
        var sut = BuildSut();

        var result = await sut.Handle(new GetMonitoringFeatureTogglesQuery(), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value!.ActivityMonitoring.Should().BeTrue();
        result.Value.ScreenshotCapture.Should().BeTrue();
        result.Value.ApplicationTracking.Should().BeFalse();
        result.Value.IdleThresholdMinutes.Should().Be(12);
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
