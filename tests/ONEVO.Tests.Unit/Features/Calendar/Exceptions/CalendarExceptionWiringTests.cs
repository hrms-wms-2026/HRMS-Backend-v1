using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.Calendar.Exceptions;
using ONEVO.Application.Features.CoreHr.Employee.DTOs.Responses;
using ONEVO.Application.Features.CoreHr.Employee.Helpers;
using ONEVO.Application.Features.Inbox.Escalation;
using ONEVO.Application.Features.Monitoring.Exceptions.RepositoryInterfaces;
using ONEVO.Domain.Features.Monitoring.Exceptions.Entities;
using Xunit;
using MonitoringException = ONEVO.Domain.Features.Monitoring.Exceptions.Entities.Exception;

namespace ONEVO.Tests.Unit.Features.Calendar.Exceptions;

/// <summary>Plan C2: calendar cases escalate through the calendar policy only, and show as their own signal.</summary>
public sealed class CalendarExceptionWiringTests
{
    private static readonly Guid TenantId = Guid.NewGuid();
    private static readonly DateTimeOffset Now = new(2026, 10, 14, 10, 0, 0, TimeSpan.Zero);

    private static MonitoringException Case(ExceptionType type, int daysOld) => new()
    {
        Id = Guid.NewGuid(), TenantId = TenantId, EmployeeId = Guid.NewGuid(), Type = type, Status = ExceptionStatus.Open,
        Title = "Missed meetings", DetectedAt = Now.AddDays(-daysOld),
    };

    private static (CalendarExceptionEscalationSweepSource Sweep, Mock<INotificationDispatcher> Notifications, Mock<IInboxEscalationPolicy> Policy, List<MonitoringException> Cases)
        Sweep(params MonitoringException[] cases)
    {
        var exceptions = new Mock<IExceptionRepository>();
        exceptions.Setup(e => e.GetStaleOpenAsync(TenantId, It.IsAny<DateTimeOffset>(), It.IsAny<CancellationToken>())).ReturnsAsync(cases.ToList());
        var policy = new Mock<IInboxEscalationPolicy>();
        policy.SetupGet(p => p.Sla).Returns(TimeSpan.FromDays(3));
        policy.Setup(p => p.InitialAsync(TenantId, It.IsAny<EscalationContext>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new EscalationTarget.Employees([new EscalationRecipient(Guid.NewGuid(), Guid.NewGuid())]));
        var registry = new Mock<IInboxEscalationPolicyRegistry>();
        registry.Setup(r => r.For("calendar", It.IsAny<string>())).Returns(policy.Object);
        var notifications = new Mock<INotificationDispatcher>();
        return (new CalendarExceptionEscalationSweepSource(exceptions.Object, registry.Object, notifications.Object,
            NullLogger<CalendarExceptionEscalationSweepSource>.Instance), notifications, policy, cases.ToList());
    }

    [Fact]
    public async Task An_overdue_calendar_case_goes_to_the_reviewers_own_manager()
    {
        var overdue = Case(ExceptionType.MeetingNoShow, 4);
        var (sweep, notifications, policy, _) = Sweep(overdue, Case(ExceptionType.MeetingRsvp, 1), Case(ExceptionType.SustainedLowActivity, 9));
        var above = Guid.NewGuid();
        policy.Setup(p => p.NextAsync(TenantId, It.Is<EscalationContext>(c => c.CurrentHolderEmployeeIds.Count == 1), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new EscalationTarget.Employees([new EscalationRecipient(above, Guid.NewGuid())]));

        var escalated = await sweep.EscalateOverdueAsync(TenantId, Now, default);

        escalated.Should().Be(1, "only the calendar case past its 3-day SLA - the monitoring case is not this sweep's");
        overdue.Status.Should().Be(ExceptionStatus.Escalated);
        notifications.Verify(n => n.SendTemplatedAsync(TenantId, above, CalendarExceptionEscalationSweepSource.EscalatedTemplate,
            It.IsAny<IReadOnlyDictionary<string, string>>(), "inbox:calendar:meeting_exception", overdue.Id, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task With_nobody_above_the_escalation_is_only_logged()
    {
        var overdue = Case(ExceptionType.MeetingConflict, 5);
        var (sweep, notifications, policy, _) = Sweep(overdue);
        policy.Setup(p => p.NextAsync(TenantId, It.IsAny<EscalationContext>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new EscalationTarget.LogOnly("No one above - logged."));

        (await sweep.EscalateOverdueAsync(TenantId, Now, default)).Should().Be(1);

        overdue.Status.Should().Be(ExceptionStatus.Escalated, "so it is not swept again");
        notifications.Verify(n => n.SendTemplatedAsync(It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<string>(),
            It.IsAny<IReadOnlyDictionary<string, string>>(), It.IsAny<string?>(), It.IsAny<Guid?>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public void Meeting_exceptions_are_their_own_signal_next_to_the_monitoring_one()
    {
        var signals = EmployeeSignalCatalogue.Build(new EmployeeSignalInputs(null, null, null, null, new EmployeeMonitoringSignalCounts(0, 2), MeetingExceptions: 3));

        signals.Select(s => (s.Key, s.Category, s.Value)).Should().Equal(("monitoring_exceptions", "monitoring", 2), ("meeting_exceptions", "meetings", 3));
        EmployeeSignalCatalogue.Build(new EmployeeSignalInputs(null, null, null, null, null)).Should().BeEmpty("hidden when the viewer may not see cases");
    }

    [Fact]
    public void Every_calendar_rule_maps_to_a_calendar_case_type_and_no_monitoring_type_is_calendar()
    {
        CalendarRuleCatalog.Specs.Values.Should().OnlyContain(s => CalendarExceptionTypes.IsCalendar(s.Type));
        CalendarRuleCatalog.Specs.Should().HaveCount(11);
        new[] { ExceptionType.SustainedLowActivity, ExceptionType.AttendanceIrregularity, ExceptionType.UnusualActivityPattern, ExceptionType.IdentityAnomaly }
            .Should().OnlyContain(t => !CalendarExceptionTypes.IsCalendar(t));
    }
}
