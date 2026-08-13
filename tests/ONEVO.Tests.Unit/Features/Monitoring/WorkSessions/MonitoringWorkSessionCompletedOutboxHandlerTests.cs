using FluentAssertions;
using Moq;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.DevPlatform.Tenancy.RepositoryInterfaces;
using ONEVO.Application.Features.Monitoring.ActivityMonitoring.ServiceInterfaces;
using ONEVO.Application.Features.Monitoring.WorkSessions.OutboxHandlers;
using ONEVO.Application.Features.Monitoring.WorkSessions.OutboxPayloads;
using ONEVO.Domain.Features.InfrastructureModule.Entities;

namespace ONEVO.Tests.Unit.Features.Monitoring.WorkSessions;

public sealed class MonitoringWorkSessionCompletedOutboxHandlerTests
{
    private readonly Mock<ITenantRepository> _tenants = new();
    private readonly Mock<ITenantContextSwitcher> _tenantSwitcher = new();
    private readonly Mock<IMonitoringReportTimeZoneResolver> _timeZoneResolver = new();
    private readonly Mock<IActivityDailySummaryRebuilder> _rebuilder = new();

    private readonly Guid _tenantId = Guid.NewGuid();
    private readonly Guid _employeeId = Guid.NewGuid();
    private readonly Guid _sessionId = Guid.NewGuid();

    public MonitoringWorkSessionCompletedOutboxHandlerTests()
    {
        _tenants.Setup(t => t.GetByIdAsync(_tenantId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Tenant
            {
                Id = _tenantId,
                Name = "Acme",
                Slug = "acme",
                Status = TenantStatus.Active
            });

        _timeZoneResolver
            .Setup(r => r.ResolveAsync(_tenantId, _employeeId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(TimeZoneInfo.Utc);
    }

    private MonitoringWorkSessionCompletedOutboxHandler CreateSut()
        => new(
            _tenants.Object,
            _tenantSwitcher.Object,
            _timeZoneResolver.Object,
            _rebuilder.Object);

    [Fact]
    public void Type_is_monitoring_work_session_completed()
    {
        CreateSut().Type.Should().Be(OutboxMessageTypes.MonitoringWorkSessionCompleted);
    }

    [Fact]
    public async Task HandleAsync_rebuilds_local_date_from_clock_out()
    {
        var clockOut = new DateTimeOffset(2026, 8, 10, 23, 30, 0, TimeSpan.Zero);
        var payload = new MonitoringWorkSessionCompletedPayload(
            _sessionId, _tenantId, _employeeId, clockOut);

        await CreateSut().HandleAsync(
            System.Text.Json.JsonSerializer.Serialize(payload),
            CancellationToken.None);

        _tenantSwitcher.Verify(
            s => s.SwitchToTenantAsync(
                It.Is<TenantRegistryEntry>(e => e.TenantId == _tenantId),
                It.IsAny<CancellationToken>()),
            Times.Once);

        _rebuilder.Verify(
            r => r.RebuildAsync(
                _tenantId,
                _employeeId,
                new DateOnly(2026, 8, 10),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task HandleAsync_is_idempotent_on_redelivery()
    {
        var payload = new MonitoringWorkSessionCompletedPayload(
            _sessionId,
            _tenantId,
            _employeeId,
            new DateTimeOffset(2026, 8, 10, 18, 0, 0, TimeSpan.Zero));

        var json = System.Text.Json.JsonSerializer.Serialize(payload);
        var sut = CreateSut();

        await sut.HandleAsync(json, CancellationToken.None);
        await sut.HandleAsync(json, CancellationToken.None);

        _rebuilder.Verify(
            r => r.RebuildAsync(
                _tenantId,
                _employeeId,
                new DateOnly(2026, 8, 10),
                It.IsAny<CancellationToken>()),
            Times.Exactly(2));
    }
}
