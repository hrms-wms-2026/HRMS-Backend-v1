using FluentAssertions;
using Moq;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.Calendar.Exceptions;
using ONEVO.Application.Features.Inbox.Escalation;
using ONEVO.Application.Features.Monitoring.Exceptions.RepositoryInterfaces;
using ONEVO.Domain.Features.Calendar.Entities;
using ONEVO.Domain.Features.CoreHr.Entities;
using ONEVO.Domain.Features.Monitoring.Exceptions.Entities;
using Xunit;
using CoreEmployeeRepository = ONEVO.Application.Features.CoreHr.Employee.RepositoryInterfaces.IEmployeeRepository;
using MonitoringException = ONEVO.Domain.Features.Monitoring.Exceptions.Entities.Exception;

namespace ONEVO.Tests.Unit.Features.Calendar.Exceptions;

/// <summary>Plan C2: incidents roll up into deduplicated meeting-exception cases, routed through the calendar policy.</summary>
public sealed class CalendarCaseServiceTests
{
    private static readonly Guid TenantId = Guid.NewGuid();
    private static readonly DateTimeOffset Now = new(2026, 10, 14, 18, 0, 0, TimeSpan.Zero);
    private readonly Employee _subject = new() { Id = Guid.NewGuid(), TenantId = TenantId, UserId = Guid.NewGuid(), FirstName = "Pat", LastName = "Doe" };
    private readonly Guid _managerUserId = Guid.NewGuid();
    private readonly Guid _managerEmployeeId = Guid.NewGuid();
    private readonly FakeIncidents _incidents = new();
    private readonly Mock<IExceptionRepository> _exceptions = new();
    private readonly Mock<IInboxEscalationPolicy> _policy = new();
    private readonly Mock<IInboxEscalationPolicyRegistry> _policies = new();
    private readonly Mock<INotificationDispatcher> _notifications = new();
    private readonly Mock<CoreEmployeeRepository> _employees = new();
    private readonly List<MonitoringException> _cases = [];

    private sealed class FakeIncidents : ICalendarIncidentRepository
    {
        public List<CalendarMeetingIncident> Rows { get; } = [];
        public Task<List<CalendarMeetingIncident>> ListForEmployeeAsync(Guid tenantId, Guid employeeId, DateTimeOffset from, CancellationToken ct)
            => Task.FromResult(Rows.Where(r => r.EmployeeId == employeeId && r.OccurrenceStart >= from).ToList());
        public Task<IReadOnlyList<CalendarMeetingIncident>> ListForExceptionAsync(Guid tenantId, Guid exceptionId, CancellationToken ct)
            => Task.FromResult<IReadOnlyList<CalendarMeetingIncident>>(Rows.Where(r => r.ExceptionId == exceptionId).ToList());
        public Task<IReadOnlyList<CalendarMeetingIncident>> ListForEventAsync(Guid tenantId, Guid eventId, CancellationToken ct)
            => Task.FromResult<IReadOnlyList<CalendarMeetingIncident>>(Rows.Where(r => r.EventId == eventId).ToList());
        public Task<int> PurgeUnattachedAsync(Guid tenantId, DateTimeOffset before, CancellationToken ct) => Task.FromResult(0);
        public void Add(CalendarMeetingIncident incident) => Rows.Add(incident);
        public void Remove(CalendarMeetingIncident incident) => Rows.Remove(incident);
        public Task SaveChangesAsync(CancellationToken ct) => Task.CompletedTask;
    }

    public CalendarCaseServiceTests()
    {
        _policies.Setup(p => p.For("calendar", It.IsAny<string>())).Returns(_policy.Object);
        _policy.Setup(p => p.InitialAsync(TenantId, It.IsAny<EscalationContext>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new EscalationTarget.Employees([new EscalationRecipient(_managerUserId, _managerEmployeeId)]));
        _employees.Setup(e => e.GetByIdAsync(TenantId, _subject.Id, It.IsAny<CancellationToken>())).ReturnsAsync(_subject);
        _exceptions.Setup(e => e.AddAsync(It.IsAny<MonitoringException>(), It.IsAny<CancellationToken>()))
            .Callback((MonitoringException e, CancellationToken _) => _cases.Add(e)).Returns(Task.CompletedTask);
        _exceptions.Setup(e => e.GetByIdAsync(TenantId, It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Guid _, Guid id, CancellationToken _) => _cases.FirstOrDefault(c => c.Id == id));
        _exceptions.Setup(e => e.GetOpenOrAcknowledgedAsync(TenantId, _subject.Id, It.IsAny<ExceptionType>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Guid _, Guid _, ExceptionType type, CancellationToken _) =>
                _cases.FirstOrDefault(c => c.Type == type && c.Status is ExceptionStatus.Open or ExceptionStatus.Acknowledged));
    }

    private CalendarCaseService Sut() => new(_incidents, _exceptions.Object, _policies.Object, _notifications.Object, _employees.Object);

    private static CalendarIncidentFinding Finding(string rule, int daysAgo, bool immediate = false, Guid? eventId = null)
        => new(rule, eventId ?? Guid.NewGuid(), Now.AddDays(-daysAgo), CalendarIncidentConfidence.Provider,
            new Dictionary<string, object?> { ["title"] = "Planning" }, immediate);

    private CalendarMeetingIncident Stored(string rule, int daysAgo, Guid? caseId = null)
    {
        var row = new CalendarMeetingIncident
        {
            Id = Guid.NewGuid(), TenantId = TenantId, EmployeeId = _subject.Id, RuleCode = rule, EventId = Guid.NewGuid(),
            OccurrenceStart = Now.AddDays(-daysAgo), ExceptionId = caseId,
        };
        _incidents.Rows.Add(row);
        return row;
    }

    private Task<CalendarCaseResult> Apply(params CalendarIncidentFinding[] findings)
        => Sut().ApplyAsync(TenantId, _subject.Id, findings, CalendarRuleCatalog.Specs.Keys.ToList(), Now, Now, default);

    [Theory]
    [InlineData(1, false)] // 2 no-shows in 30 days
    [InlineData(2, true)]  // 3 no-shows in 30 days
    public async Task A_threshold_rule_opens_a_case_exactly_at_its_threshold(int earlier, bool opens)
    {
        for (var i = 0; i < earlier; i++)
            Stored(CalendarRuleCodes.NoShow, 5 + i);

        var result = await Apply(Finding(CalendarRuleCodes.NoShow, 1));

        result.CasesOpened.Should().HaveCount(opens ? 1 : 0);
        if (opens)
        {
            _cases.Single().Type.Should().Be(ExceptionType.MeetingNoShow);
            _incidents.Rows.Should().OnlyContain(i => i.ExceptionId == _cases.Single().Id, "every counted incident is the case's evidence");
        }
    }

    [Fact]
    public async Task Incidents_older_than_the_window_do_not_count()
    {
        Stored(CalendarRuleCodes.NoShow, 31);
        Stored(CalendarRuleCodes.NoShow, 35);

        (await Apply(Finding(CalendarRuleCodes.NoShow, 1))).CasesOpened.Should().BeEmpty();
    }

    [Fact]
    public async Task Incidents_on_a_dismissed_case_stop_counting()
    {
        var dismissed = new MonitoringException
        {
            Id = Guid.NewGuid(), TenantId = TenantId, EmployeeId = _subject.Id, Type = ExceptionType.MeetingNoShow,
            Status = ExceptionStatus.Resolved, Outcome = ExceptionOutcomes.Excused,
        };
        _cases.Add(dismissed);
        Stored(CalendarRuleCodes.NoShow, 5, dismissed.Id);
        Stored(CalendarRuleCodes.NoShow, 6, dismissed.Id);

        (await Apply(Finding(CalendarRuleCodes.NoShow, 1))).CasesOpened.Should().BeEmpty();
    }

    [Fact]
    public async Task An_immediate_finding_opens_a_case_and_tells_the_manager_and_the_employee()
    {
        var result = await Apply(Finding(CalendarRuleCodes.OnLeave, -2, immediate: true));

        var opened = _cases.Should().ContainSingle().Subject;
        result.CasesOpened.Should().Equal(opened.Id);
        (opened.Type, opened.Status).Should().Be((ExceptionType.MeetingConflict, ExceptionStatus.Open));
        opened.MetadataJson.Should().Contain("\"source\":\"calendar\"").And.Contain(CalendarRuleCodes.OnLeave);
        _notifications.Verify(n => n.SendTemplatedAsync(TenantId, _managerUserId, CalendarCaseService.ReviewerTemplate,
            It.IsAny<IReadOnlyDictionary<string, string>>(), "inbox:calendar:meeting_exception", opened.Id, It.IsAny<CancellationToken>()), Times.Once);
        _notifications.Verify(n => n.SendTemplatedAsync(TenantId, _subject.UserId, CalendarCaseService.SubjectTemplate,
            It.IsAny<IReadOnlyDictionary<string, string>>(), "inbox:calendar:meeting_exception", opened.Id, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task A_new_incident_attaches_to_the_open_case_of_its_type_instead_of_opening_another()
    {
        var open = new MonitoringException
        {
            Id = Guid.NewGuid(), TenantId = TenantId, EmployeeId = _subject.Id, Type = ExceptionType.MeetingConflict, Status = ExceptionStatus.Acknowledged,
        };
        _cases.Add(open);

        var result = await Apply(Finding(CalendarRuleCodes.OnLeave, -2, immediate: true));

        result.CasesOpened.Should().BeEmpty();
        _cases.Should().ContainSingle();
        _incidents.Rows.Single().ExceptionId.Should().Be(open.Id);
        _notifications.Verify(n => n.SendTemplatedAsync(It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<string>(),
            It.IsAny<IReadOnlyDictionary<string, string>>(), It.IsAny<string?>(), It.IsAny<Guid?>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task With_no_reviewer_above_the_case_is_only_logged_and_the_employee_still_told()
    {
        _policy.Setup(p => p.InitialAsync(TenantId, It.IsAny<EscalationContext>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new EscalationTarget.LogOnly("No reporting manager found - logged."));

        await Apply(Finding(CalendarRuleCodes.Unresolved, 1, immediate: true));

        _notifications.Verify(n => n.SendTemplatedAsync(TenantId, _managerUserId, It.IsAny<string>(),
            It.IsAny<IReadOnlyDictionary<string, string>>(), It.IsAny<string?>(), It.IsAny<Guid?>(), It.IsAny<CancellationToken>()), Times.Never);
        _notifications.Verify(n => n.SendTemplatedAsync(TenantId, _subject.UserId, CalendarCaseService.SubjectTemplate,
            It.IsAny<IReadOnlyDictionary<string, string>>(), It.IsAny<string?>(), It.IsAny<Guid?>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task The_same_meeting_occurrence_is_recorded_only_once()
    {
        var finding = Finding(CalendarRuleCodes.Late, 1);
        await Apply(finding);
        await Apply(finding);

        _incidents.Rows.Should().ContainSingle();
    }

    [Fact]
    public async Task A_declined_meeting_during_leave_takes_its_incident_and_its_case_away()
    {
        var onLeave = Finding(CalendarRuleCodes.OnLeave, -2, immediate: true);
        await Apply(onLeave);
        var opened = _cases.Single();

        // Next evaluation: the employee declined, so the rule no longer finds it.
        var result = await Apply();

        result.IncidentsRemoved.Should().Be(1);
        _incidents.Rows.Should().BeEmpty();
        (opened.Status, opened.Outcome).Should().Be((ExceptionStatus.Resolved, ExceptionOutcomes.Excused));
    }
}
