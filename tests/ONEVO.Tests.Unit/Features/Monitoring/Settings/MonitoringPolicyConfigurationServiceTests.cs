using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using MediatR;
using Moq;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.Monitoring.Settings.ServiceInterfaces;
using ONEVO.Domain.Features.Monitoring.Settings.Entities;
using TimeAttendanceWorkMode = ONEVO.Domain.Features.TimeAttendance.Entities.WorkMode;
using ONEVO.Infrastructure.Persistence;
using ONEVO.Infrastructure.Persistence.Interceptors;
using ONEVO.Infrastructure.Services.Monitoring.Settings;
using Xunit;

namespace ONEVO.Tests.Unit.Features.Monitoring.Settings;

public class MonitoringPolicyConfigurationServiceTests
{
    private static ApplicationDbContext BuildInMemoryDb()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .ConfigureWarnings(warnings => warnings.Ignore(InMemoryEventId.TransactionIgnoredWarning))
            .Options;

        return new ApplicationDbContext(
            options,
            new AuditableEntityInterceptor(new Mock<ICurrentUser>().Object, new Mock<IDateTimeProvider>().Object),
            new SoftDeleteInterceptor(new Mock<IDateTimeProvider>().Object),
            new DomainEventDispatchInterceptor(new Mock<IPublisher>().Object),
            new Mock<ITenantContext>().Object);
    }

    [Fact]
    public async Task UpsertOverrideAsync_WorkModeScope_ForExistingActiveWorkMode_Succeeds()
    {
        // Arrange
        await using var db = BuildInMemoryDb();
        var tenantId = Guid.NewGuid();
        var legalEntityId = Guid.NewGuid();
        var workModeId = Guid.NewGuid();
        var actorId = Guid.NewGuid();
        var now = DateTimeOffset.UtcNow;

        db.TimeAttendanceWorkModes.Add(
            new TimeAttendanceWorkMode
            {
                Id = workModeId,
                TenantId = tenantId,
                LegalEntityId = legalEntityId,
                Name = "Remote",
                IsActive = true,
                CreatedAt = now,
                UpdatedAt = now
            });
        await db.SaveChangesAsync();

        var cacheMock = new Mock<ICacheService>();
        var service = new MonitoringPolicyConfigurationService(db, cacheMock.Object);
        var request = new MonitoringPolicyOverrideRequest(
            ActivityMonitoring: true,
            ApplicationTracking: false,
            DocumentTracking: false,
            CommunicationTracking: false,
            ScreenshotCapture: false,
            AutoScreenshotCapture: false,
            MeetingDetection: false,
            DeviceTracking: false,
            WorkLocationVerification: false,
            IdentityVerification: false,
            Biometric: false,
            IdleThresholdMinutes: 120,
            OverrideReason: "test",
            AllowedRadiusMeters: 150);

        // Act
        var result = await service.UpsertOverrideAsync(
            tenantId,
            actorId,
            "work_mode",
            workModeId,
            legalEntityId,
            request,
            default);

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.Value.Should().NotBeNull();
        result.Value!.ScopeType.Should().Be("work_mode");
        result.Value!.AllowedRadiusMeters.Should().Be(150);

        var persisted = await db.MonitoringPolicyOverrides
            .FirstOrDefaultAsync(x => x.TenantId == tenantId && x.ScopeType == "work_mode" && x.ScopeId == workModeId);
        persisted.Should().NotBeNull();
        persisted!.AllowedRadiusMeters.Should().Be(150);
    }

    [Fact]
    public async Task UpsertOverrideAsync_WorkModeScope_ForInactiveWorkMode_ReturnsNotFound()
    {
        // Arrange
        await using var db = BuildInMemoryDb();
        var tenantId = Guid.NewGuid();
        var legalEntityId = Guid.NewGuid();
        var workModeId = Guid.NewGuid();
        var actorId = Guid.NewGuid();
        var now = DateTimeOffset.UtcNow;

        db.TimeAttendanceWorkModes.Add(
            new TimeAttendanceWorkMode
            {
                Id = workModeId,
                TenantId = tenantId,
                LegalEntityId = legalEntityId,
                Name = "Archived",
                IsActive = false,
                CreatedAt = now,
                UpdatedAt = now
            });
        await db.SaveChangesAsync();

        var cacheMock = new Mock<ICacheService>();
        var service = new MonitoringPolicyConfigurationService(db, cacheMock.Object);
        var request = new MonitoringPolicyOverrideRequest(
            ActivityMonitoring: true,
            ApplicationTracking: false,
            DocumentTracking: false,
            CommunicationTracking: false,
            ScreenshotCapture: false,
            AutoScreenshotCapture: false,
            MeetingDetection: false,
            DeviceTracking: false,
            WorkLocationVerification: false,
            IdentityVerification: false,
            Biometric: false,
            IdleThresholdMinutes: 120,
            OverrideReason: "test",
            AllowedRadiusMeters: 150);

        // Act
        var result = await service.UpsertOverrideAsync(
            tenantId,
            actorId,
            "work_mode",
            workModeId,
            legalEntityId,
            request,
            default);

        // Assert
        result.IsSuccess.Should().BeFalse();
        result.StatusCode.Should().Be(404);
    }

    [Fact]
    public async Task UpsertOverrideAsync_WorkModeScope_FromDifferentLegalEntity_ReturnsNotFound()
    {
        // Arrange
        await using var db = BuildInMemoryDb();
        var tenantId = Guid.NewGuid();
        var legalEntityId = Guid.NewGuid();
        var otherLegalEntityId = Guid.NewGuid();
        var workModeId = Guid.NewGuid();
        var actorId = Guid.NewGuid();
        var now = DateTimeOffset.UtcNow;

        db.TimeAttendanceWorkModes.Add(
            new TimeAttendanceWorkMode
            {
                Id = workModeId,
                TenantId = tenantId,
                LegalEntityId = otherLegalEntityId,
                Name = "Remote",
                IsActive = true,
                CreatedAt = now,
                UpdatedAt = now
            });
        await db.SaveChangesAsync();

        var cacheMock = new Mock<ICacheService>();
        var service = new MonitoringPolicyConfigurationService(db, cacheMock.Object);
        var request = new MonitoringPolicyOverrideRequest(
            ActivityMonitoring: true,
            ApplicationTracking: false,
            DocumentTracking: false,
            CommunicationTracking: false,
            ScreenshotCapture: false,
            AutoScreenshotCapture: false,
            MeetingDetection: false,
            DeviceTracking: false,
            WorkLocationVerification: false,
            IdentityVerification: false,
            Biometric: false,
            IdleThresholdMinutes: 120,
            OverrideReason: "test",
            AllowedRadiusMeters: 150);

        // Act
        var result = await service.UpsertOverrideAsync(
            tenantId,
            actorId,
            "work_mode",
            workModeId,
            legalEntityId,
            request,
            default);

        // Assert
        result.IsSuccess.Should().BeFalse();
        result.StatusCode.Should().Be(404);
    }
}
