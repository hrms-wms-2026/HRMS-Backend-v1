using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using IUnitOfWork = ONEVO.Application.Common.RepositoryInterfaces.IUnitOfWork;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.Auth.Permission.RepositoryInterfaces;
using ONEVO.Application.Features.CoreHr.Employee.RepositoryInterfaces;
using ONEVO.Application.Features.CoreHr.EmployeeAuthority.ServiceInterfaces;
using ONEVO.Application.Features.Inbox.Escalation;
using ONEVO.Application.Features.Monitoring.Exceptions.Inbox;
using ONEVO.Application.Features.Monitoring.Exceptions.RepositoryInterfaces;
using ONEVO.Application.Features.Monitoring.Exceptions.ServiceInterfaces;
using ONEVO.Domain.Features.Monitoring.Exceptions.Entities;
using ONEVO.Tests.Unit.Fakes;
using Xunit;
using MonitoringException = ONEVO.Domain.Features.Monitoring.Exceptions.Entities.Exception;

namespace ONEVO.Tests.Unit.Features.Monitoring.Exceptions;

/// <summary>
/// The 3-day stale sweep moved from ExceptionDetectionJob (nightly) to the inbox escalation sweep (hourly).
/// Characterisation: for the existing exception types the result is the old sweep's - status Escalated,
/// EscalatedAt = now, EscalatedById null (a sweep, not a person), the HR "escalated" alert through the
/// tenant's alert router. Each case is saved right after its alert is queued (BE-11: a failed inbox record of one
/// case must never sink the others).
/// </summary>
public class ExceptionEscalationSweepSourceTests
{
    private static readonly Guid TenantId = Guid.NewGuid();
    private static readonly DateTimeOffset Now = new(2026, 9, 16, 0, 0, 0, TimeSpan.Zero);

    private readonly Mock<IExceptionRepository> _exceptions = new();
    private readonly Mock<IExceptionAlertRouter> _router = new();
    private readonly Mock<IExceptionAlertRouterFactory> _routers = new();
    private readonly Mock<IUnitOfWork> _unitOfWork = new();
    private readonly List<string> _log = new();

    public ExceptionEscalationSweepSourceTests()
    {
        _routers.Setup(f => f.CreateForTenant(TenantId)).Returns(_router.Object);
        _router.Setup(r => r.NotifyEscalatedAsync(It.IsAny<MonitoringException>(), It.IsAny<CancellationToken>()))
            .Callback((MonitoringException e, CancellationToken _) => _log.Add($"escalated:{e.Id}"))
            .Returns(Task.CompletedTask);
        _exceptions.Setup(e => e.SaveChangesAsync(It.IsAny<CancellationToken>())).Callback(() => _log.Add("save")).ReturnsAsync(1);
    }

    private static MonitoringException Case(ExceptionType type, int daysOld, ExceptionStatus status = ExceptionStatus.Open) => new()
    {
        Id = Guid.NewGuid(), TenantId = TenantId, EmployeeId = Guid.NewGuid(), Type = type, Status = status,
        Title = type.ToString(), Description = "d", DetectedAt = Now.AddDays(-daysOld),
    };

    private ExceptionEscalationSweepSource Source(IInboxEscalationPolicy? policy = null, IInboxEscalationRecorder? recorder = null)
    {
        policy ??= new ReportingLineThenHrPolicy(_ => Mock.Of<IEmployeeAuthorityResolver>(), Mock.Of<IEmployeeRepository>(),
            Mock.Of<IPermissionRepository>(), new FakeDateTimeProvider { UtcNow = Now });
        return new ExceptionEscalationSweepSource(_exceptions.Object, _routers.Object, new InboxEscalationPolicyRegistry([policy]),
            _unitOfWork.Object, NullLogger<ExceptionEscalationSweepSource>.Instance, recorder);
    }

    [Fact]
    public async Task A_recorder_failure_drops_its_half_written_rows_so_the_other_escalations_still_save()
    {
        var failing = Case(ExceptionType.SustainedLowActivity, 5);
        var next = Case(ExceptionType.SustainedLowActivity, 5);
        _exceptions.Setup(e => e.GetStaleOpenAsync(TenantId, It.IsAny<DateTimeOffset>(), It.IsAny<CancellationToken>())).ReturnsAsync([failing, next]);
        var policy = new Mock<IInboxEscalationPolicy>();
        policy.Setup(p => p.AppliesTo(It.IsAny<string>(), It.IsAny<string>())).Returns(true);
        policy.SetupGet(p => p.Sla).Returns(TimeSpan.FromDays(3));
        policy.Setup(p => p.NextAsync(TenantId, It.IsAny<EscalationContext>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new EscalationTarget.LogOnly("top"));
        var recorder = new Mock<IInboxEscalationRecorder>();
        recorder.Setup(r => r.RecordAsync(TenantId, It.IsAny<EscalationRecord>(), It.IsAny<CancellationToken>()))
            .Callback((Guid _, EscalationRecord r, CancellationToken _) => _log.Add($"record:{r.Item.Id}"))
            .ReturnsAsync([]);
        recorder.Setup(r => r.RecordAsync(TenantId, It.Is<EscalationRecord>(x => x.Item.Id == failing.Id), It.IsAny<CancellationToken>()))
            .Callback(() => _log.Add($"record:{failing.Id}"))
            .ThrowsAsync(new InvalidOperationException("conversation insert failed"));
        _unitOfWork.Setup(u => u.ClearTracking()).Callback(() => _log.Add("clear"));

        var escalated = await Source(policy.Object, recorder.Object).EscalateOverdueAsync(TenantId, Now, default);

        escalated.Should().Be(2);
        // Each escalation is saved before its inbox record; the failed record's leftovers are dropped before the next save.
        _log.Should().Equal(
            $"escalated:{failing.Id}", "save", $"record:{failing.Id}", "clear",
            $"escalated:{next.Id}", "save", $"record:{next.Id}");
    }

    [Fact]
    public async Task Stale_open_cases_of_every_existing_type_are_escalated_exactly_like_the_old_nightly_sweep()
    {
        // Calendar meeting-exception cases are escalated by the calendar sweep (plan C2), never here.
        var stale = Enum.GetValues<ExceptionType>().Where(t => !CalendarExceptionTypes.IsCalendar(t)).Select(t => Case(t, 5)).ToList();
        var calendarCase = Case(ExceptionType.MeetingNoShow, 5);
        _exceptions.Setup(e => e.GetStaleOpenAsync(TenantId, Now.AddDays(-3), It.IsAny<CancellationToken>())).ReturnsAsync(stale.Append(calendarCase).ToList());

        var escalated = await Source().EscalateOverdueAsync(TenantId, Now, default);

        escalated.Should().Be(stale.Count);
        stale.Should().AllSatisfy(e =>
        {
            e.Status.Should().Be(ExceptionStatus.Escalated);
            e.EscalatedAt.Should().Be(Now);
            e.EscalatedById.Should().BeNull();
        });
        _log.Should().Equal(stale.SelectMany(e => new[] { $"escalated:{e.Id}", "save" }));
        calendarCase.Status.Should().Be(ExceptionStatus.Open);
        _exceptions.Verify(e => e.Update(It.IsAny<MonitoringException>()), Times.Exactly(stale.Count));
    }

    [Fact]
    public async Task Only_overdue_open_cases_are_touched()
    {
        var fresh = Case(ExceptionType.SustainedLowActivity, 2);
        var acknowledged = Case(ExceptionType.SustainedLowActivity, 9, ExceptionStatus.Acknowledged);
        _exceptions.Setup(e => e.GetStaleOpenAsync(TenantId, It.IsAny<DateTimeOffset>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([fresh, acknowledged]);

        var escalated = await Source().EscalateOverdueAsync(TenantId, Now, default);

        escalated.Should().Be(0);
        fresh.Status.Should().Be(ExceptionStatus.Open);
        acknowledged.Status.Should().Be(ExceptionStatus.Acknowledged);
        _exceptions.Verify(e => e.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task An_alert_failure_still_saves_the_escalation()
    {
        var stale = Case(ExceptionType.UnusualActivityPattern, 4);
        _exceptions.Setup(e => e.GetStaleOpenAsync(TenantId, It.IsAny<DateTimeOffset>(), It.IsAny<CancellationToken>())).ReturnsAsync([stale]);
        _router.Setup(r => r.NotifyEscalatedAsync(It.IsAny<MonitoringException>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("boom"));

        await Source().EscalateOverdueAsync(TenantId, Now, default);

        stale.Status.Should().Be(ExceptionStatus.Escalated);
        _log.Should().Equal("save");
    }
}
