using MediatR;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.Monitoring.ActivityMonitoring.DTOs.Responses;
using ONEVO.Application.Features.Monitoring.ActivityMonitoring.RepositoryInterfaces;
using ONEVO.Application.Features.Monitoring.ActivityMonitoring.ServiceInterfaces;
using ONEVO.Application.Features.Monitoring.ActivityMonitoring.Services;
using ONEVO.Application.Features.Monitoring.ActivityMonitoring.Queries.GetActivityDailySummary;
using ONEVO.Application.Features.Monitoring.Screenshots;
using ONEVO.Application.Features.Monitoring.Screenshots.RepositoryInterfaces;
using ONEVO.Application.Features.Monitoring.WorkSessions.RepositoryInterfaces;
using ONEVO.Domain.Features.Monitoring.Screenshots.Entities;
using ONEVO.Domain.Features.Monitoring.WorkSessions.Entities;

namespace ONEVO.Application.Features.Monitoring.ActivityMonitoring.Queries.GetEmployeeDailyMonitoringReport;

public sealed class GetEmployeeDailyMonitoringReportQueryHandler
    : IRequestHandler<GetEmployeeDailyMonitoringReportQuery, Result<EmployeeDailyMonitoringReportDto>>
{
    private readonly IActivityDailySummaryRepository _summaries;
    private readonly IInactivityCaptureAttemptRepository _attempts;
    private readonly IWorkSessionRepository _workSessions;
    private readonly IMonitoringReportTimeZoneResolver _timeZoneResolver;
    private readonly ITenantContext _tenantContext;

    public GetEmployeeDailyMonitoringReportQueryHandler(
        IActivityDailySummaryRepository summaries,
        IInactivityCaptureAttemptRepository attempts,
        IWorkSessionRepository workSessions,
        IMonitoringReportTimeZoneResolver timeZoneResolver,
        ITenantContext tenantContext)
    {
        _summaries = summaries;
        _attempts = attempts;
        _workSessions = workSessions;
        _timeZoneResolver = timeZoneResolver;
        _tenantContext = tenantContext;
    }

    public async Task<Result<EmployeeDailyMonitoringReportDto>> Handle(
        GetEmployeeDailyMonitoringReportQuery request,
        CancellationToken cancellationToken)
    {
        if (_tenantContext.TenantId == Guid.Empty)
            return Result<EmployeeDailyMonitoringReportDto>.Failure("Tenant context is required.", 401);

        if (request.EmployeeId == Guid.Empty)
            return Result<EmployeeDailyMonitoringReportDto>.Failure("employeeId is required.", 400);

        var tenantId = _tenantContext.TenantId;
        var timeZone = await _timeZoneResolver.ResolveAsync(
            tenantId, request.EmployeeId, cancellationToken);

        var (fromUtc, toUtc) = MonitoringReportDateRange.ToUtcBounds(request.Date, timeZone);

        var attempts = await _attempts.GetByEmployeeRangeAsync(
            tenantId, request.EmployeeId, fromUtc, toUtc, cancellationToken);

        var sessions = await _workSessions.GetByEmployeeRangeAsync(
            tenantId, request.EmployeeId, fromUtc, toUtc, cancellationToken);

        var activityEntity = await _summaries.GetAsync(
            tenantId, request.EmployeeId, request.Date, cancellationToken);

        var report = new EmployeeDailyMonitoringReportDto(
            EmployeeId: request.EmployeeId,
            Date: request.Date,
            Activity: activityEntity is null
                ? null
                : GetActivityDailySummaryQueryHandler.Map(activityEntity),
            WorkSessions: sessions.Select(MapWorkSession).ToList(),
            PromptCount: attempts.Count,
            CapturedCount: CountOutcome(attempts, InactivityCaptureOutcomes.Captured),
            DeclinedCount: CountOutcome(attempts, InactivityCaptureOutcomes.Declined),
            TimedOutCount: CountOutcome(attempts, InactivityCaptureOutcomes.TimedOut),
            ActivityResumedCount: CountOutcome(attempts, InactivityCaptureOutcomes.ActivityResumed),
            MonitoringStoppedCount: CountOutcome(attempts, InactivityCaptureOutcomes.MonitoringStopped),
            FailedCount: CountOutcome(attempts, InactivityCaptureOutcomes.CaptureFailed),
            InactivityAttempts: attempts.Select(MapAttempt).ToList());

        return Result<EmployeeDailyMonitoringReportDto>.Success(report);
    }

    private static int CountOutcome(
        IReadOnlyList<InactivityCaptureAttempt> attempts,
        string outcome)
        => attempts.Count(a => string.Equals(a.Outcome, outcome, StringComparison.OrdinalIgnoreCase));

    private static WorkSessionReportDto MapWorkSession(EmployeeWorkSession session)
        => new(
            SessionId: session.Id,
            ClockInAt: session.ClockInAt,
            ClockOutAt: session.ClockOutAt,
            WorkSeconds: session.AccumulatedWorkSeconds,
            BreakSeconds: session.AccumulatedBreakSeconds,
            BreakCount: session.BreakSessionCount);

    private static InactivityAttemptReportDto MapAttempt(InactivityCaptureAttempt attempt)
    {
        var captured = string.Equals(
            attempt.Outcome, InactivityCaptureOutcomes.Captured, StringComparison.OrdinalIgnoreCase);

        return new InactivityAttemptReportDto(
            AttemptId: attempt.Id,
            PromptedAt: attempt.PromptedAt,
            CapturedAt: attempt.CapturedAt,
            IdleDurationSeconds: attempt.IdleDurationSeconds,
            MonitorCount: attempt.MonitorCount,
            Outcome: attempt.Outcome,
            FailureCode: attempt.FailureCode,
            EvidenceAssetId: captured ? attempt.EvidenceAssetId : null,
            ScreenshotAvailable: captured && attempt.EvidenceAssetId is not null);
    }
}
