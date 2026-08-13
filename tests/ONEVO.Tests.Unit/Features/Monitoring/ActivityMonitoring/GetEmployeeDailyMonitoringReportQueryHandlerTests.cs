using FluentAssertions;
using Moq;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.Monitoring.ActivityMonitoring.Queries.GetEmployeeDailyMonitoringReport;
using ONEVO.Application.Features.Monitoring.ActivityMonitoring.RepositoryInterfaces;
using ONEVO.Application.Features.Monitoring.ActivityMonitoring.ServiceInterfaces;
using ONEVO.Application.Features.Monitoring.Screenshots;
using ONEVO.Application.Features.Monitoring.Screenshots.RepositoryInterfaces;
using ONEVO.Application.Features.Monitoring.WorkSessions.RepositoryInterfaces;
using ONEVO.Domain.Features.Monitoring.ActivityMonitoring.Entities;
using ONEVO.Domain.Features.Monitoring.Screenshots.Entities;
using ONEVO.Domain.Features.Monitoring.WorkSessions.Entities;

namespace ONEVO.Tests.Unit.Features.Monitoring.ActivityMonitoring;

public sealed class GetEmployeeDailyMonitoringReportQueryHandlerTests
{
    private readonly Mock<IActivityDailySummaryRepository> _summaries = new();
    private readonly Mock<IInactivityCaptureAttemptRepository> _attempts = new();
    private readonly Mock<IWorkSessionRepository> _workSessions = new();
    private readonly Mock<IMonitoringReportTimeZoneResolver> _timeZoneResolver = new();
    private readonly Mock<ITenantContext> _tenantContext = new();

    private readonly Guid _tenantId = Guid.NewGuid();
    private readonly Guid _employeeId = Guid.NewGuid();
    private readonly DateOnly _date = new(2026, 8, 10);

    public GetEmployeeDailyMonitoringReportQueryHandlerTests()
    {
        _tenantContext.Setup(t => t.TenantId).Returns(_tenantId);
        _timeZoneResolver
            .Setup(r => r.ResolveAsync(_tenantId, _employeeId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(TimeZoneInfo.Utc);
    }

    private GetEmployeeDailyMonitoringReportQueryHandler CreateSut()
        => new(
            _summaries.Object,
            _attempts.Object,
            _workSessions.Object,
            _timeZoneResolver.Object,
            _tenantContext.Object);

    [Fact]
    public async Task Handle_returns_prompt_and_outcome_counts()
    {
        var evidenceId = Guid.NewGuid();
        var attempts = new List<InactivityCaptureAttempt>
        {
            Attempt(InactivityCaptureOutcomes.Captured, evidenceId),
            Attempt(InactivityCaptureOutcomes.Captured, Guid.NewGuid()),
            Attempt(InactivityCaptureOutcomes.Declined, null),
            Attempt(InactivityCaptureOutcomes.TimedOut, null),
        };

        _attempts.Setup(r => r.GetByEmployeeRangeAsync(
                _tenantId, _employeeId, It.IsAny<DateTimeOffset>(), It.IsAny<DateTimeOffset>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(attempts);

        _workSessions.Setup(r => r.GetByEmployeeRangeAsync(
                _tenantId, _employeeId, It.IsAny<DateTimeOffset>(), It.IsAny<DateTimeOffset>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Array.Empty<EmployeeWorkSession>());

        _summaries.Setup(r => r.GetAsync(_tenantId, _employeeId, _date, It.IsAny<CancellationToken>()))
            .ReturnsAsync((ActivityDailySummary?)null);

        var result = await CreateSut().Handle(
            new GetEmployeeDailyMonitoringReportQuery { EmployeeId = _employeeId, Date = _date },
            CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().BeEquivalentTo(new
        {
            EmployeeId = _employeeId,
            Date = _date,
            PromptCount = 4,
            CapturedCount = 2,
            DeclinedCount = 1,
            TimedOutCount = 1,
            ActivityResumedCount = 0,
            MonitoringStoppedCount = 0,
            FailedCount = 0,
        });
    }

    [Fact]
    public async Task Handle_captured_attempts_expose_evidence_and_availability()
    {
        var evidenceId = Guid.NewGuid();
        _attempts.Setup(r => r.GetByEmployeeRangeAsync(
                _tenantId, _employeeId, It.IsAny<DateTimeOffset>(), It.IsAny<DateTimeOffset>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<InactivityCaptureAttempt>
            {
                Attempt(InactivityCaptureOutcomes.Captured, evidenceId),
                Attempt(InactivityCaptureOutcomes.Declined, null),
            });

        _workSessions.Setup(r => r.GetByEmployeeRangeAsync(
                _tenantId, _employeeId, It.IsAny<DateTimeOffset>(), It.IsAny<DateTimeOffset>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Array.Empty<EmployeeWorkSession>());

        _summaries.Setup(r => r.GetAsync(_tenantId, _employeeId, _date, It.IsAny<CancellationToken>()))
            .ReturnsAsync((ActivityDailySummary?)null);

        var result = await CreateSut().Handle(
            new GetEmployeeDailyMonitoringReportQuery { EmployeeId = _employeeId, Date = _date },
            CancellationToken.None);

        var captured = result.Value!.InactivityAttempts.Single(a => a.Outcome == InactivityCaptureOutcomes.Captured);
        captured.EvidenceAssetId.Should().Be(evidenceId);
        captured.ScreenshotAvailable.Should().BeTrue();

        var declined = result.Value.InactivityAttempts.Single(a => a.Outcome == InactivityCaptureOutcomes.Declined);
        declined.EvidenceAssetId.Should().BeNull();
        declined.ScreenshotAvailable.Should().BeFalse();
    }

    [Fact]
    public async Task Handle_includes_work_sessions_and_activity_summary()
    {
        var sessionId = Guid.NewGuid();
        _attempts.Setup(r => r.GetByEmployeeRangeAsync(
                _tenantId, _employeeId, It.IsAny<DateTimeOffset>(), It.IsAny<DateTimeOffset>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Array.Empty<InactivityCaptureAttempt>());

        _workSessions.Setup(r => r.GetByEmployeeRangeAsync(
                _tenantId, _employeeId, It.IsAny<DateTimeOffset>(), It.IsAny<DateTimeOffset>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<EmployeeWorkSession>
            {
                new()
                {
                    Id = sessionId,
                    TenantId = _tenantId,
                    UserId = _employeeId,
                    ClockInAt = new DateTimeOffset(2026, 8, 10, 9, 0, 0, TimeSpan.Zero),
                    ClockOutAt = new DateTimeOffset(2026, 8, 10, 17, 0, 0, TimeSpan.Zero),
                    AccumulatedWorkSeconds = 28_800,
                    AccumulatedBreakSeconds = 1_800,
                    BreakSessionCount = 2,
                }
            });

        _summaries.Setup(r => r.GetAsync(_tenantId, _employeeId, _date, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ActivityDailySummary
            {
                TenantId = _tenantId,
                EmployeeId = _employeeId,
                Date = _date,
                TotalActiveMinutes = 400,
                TotalIdleMinutes = 80,
                ActivePercentage = 83.33m,
                ActivityScore = 70m,
                KeyboardTotal = 1000,
                MouseTotal = 500,
                FocusMinutes = 120,
                DeepFocusSessionsCount = 2,
                IntensityAvg = 75m,
                DataCoveragePercentage = 100m,
                TopAppsJson = "[]",
                DataSource = "agent_windows",
                CreatedAt = DateTimeOffset.UtcNow,
                UpdatedAt = DateTimeOffset.UtcNow,
            });

        var result = await CreateSut().Handle(
            new GetEmployeeDailyMonitoringReportQuery { EmployeeId = _employeeId, Date = _date },
            CancellationToken.None);

        result.Value!.WorkSessions.Should().ContainSingle()
            .Which.Should().BeEquivalentTo(new
            {
                SessionId = sessionId,
                WorkSeconds = 28_800,
                BreakSeconds = 1_800,
                BreakCount = 2,
            });

        result.Value.Activity.Should().NotBeNull();
        result.Value.Activity!.TotalActiveMinutes.Should().Be(400);
    }

    private InactivityCaptureAttempt Attempt(string outcome, Guid? evidenceAssetId)
        => new()
        {
            Id = Guid.NewGuid(),
            TenantId = _tenantId,
            EmployeeId = _employeeId,
            AgentDeviceId = Guid.NewGuid(),
            IdleStartedAt = new DateTimeOffset(2026, 8, 10, 12, 0, 0, TimeSpan.Zero),
            PromptedAt = new DateTimeOffset(2026, 8, 10, 12, 5, 0, TimeSpan.Zero),
            IdleDurationSeconds = 300,
            MonitorCount = outcome == InactivityCaptureOutcomes.Captured ? 2 : 0,
            Outcome = outcome,
            EvidenceAssetId = evidenceAssetId,
            PolicyVersion = "policy-1",
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow,
        };
}
