using System.Text.Json;
using FluentAssertions;
using MediatR;
using Moq;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Common.RepositoryInterfaces;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.Calendar.Exceptions;
using ONEVO.Application.Features.Calendar.Inbox;
using ONEVO.Application.Features.Calendar.RepositoryInterfaces;
using ONEVO.Application.Features.Conversations.Contracts;
using ONEVO.Application.Features.Conversations.RepositoryInterfaces;
using ONEVO.Application.Features.Inbox.Contracts;
using ONEVO.Application.Features.Inbox.Escalation;
using ONEVO.Application.Features.Monitoring.Exceptions.Commands.EscalateException;
using ONEVO.Application.Features.Monitoring.Exceptions.Inbox;
using ONEVO.Application.Features.Monitoring.Exceptions.RepositoryInterfaces;
using ONEVO.Application.Features.Monitoring.Exceptions.ServiceInterfaces;
using ONEVO.Domain.Features.Calendar.Entities;
using ONEVO.Domain.Features.CoreHr.Entities;
using ONEVO.Domain.Features.Monitoring.Exceptions.Entities;
using ONEVO.Tests.Unit.Fakes;
using ONEVO.Tests.Unit.Features.Inbox;
using Xunit;
using MonitoringException = ONEVO.Domain.Features.Monitoring.Exceptions.Entities.Exception;

namespace ONEVO.Tests.Unit.Features.Calendar.Exceptions;

/// <summary>Plan C3: calendar meeting-exception cases in the Inbox - scope = act, meeting evidence only, X1 actions.</summary>
public sealed class CalendarInboxTests
{
    private static readonly Guid TenantId = Guid.NewGuid();
    private static readonly DateTimeOffset Now = new(2026, 10, 14, 10, 0, 0, TimeSpan.Zero);
    private readonly Employee _subject = new() { Id = Guid.NewGuid(), TenantId = TenantId, UserId = Guid.NewGuid(), FirstName = "Pat", LastName = "Doe" };
    private readonly Guid _manager = Guid.NewGuid();
    private readonly Mock<IExceptionRepository> _exceptions = new();
    private readonly Mock<IExceptionScopeResolver> _scope = new();
    private readonly Mock<IEmployeeRepository> _employees = new();
    private readonly Mock<ICalendarIncidentRepository> _incidents = new();
    private readonly Mock<ICalendarEventRepository> _events = new();
    private readonly Mock<IConversationRepository> _conversations = new();
    private readonly Mock<ISender> _sender = new();
    private readonly Mock<IInboxEscalationPolicyRegistry> _policies = new();
    private readonly Mock<IInboxEscalationRecorder> _recorder = new();
    private readonly Mock<INotificationDispatcher> _notifications = new();
    private readonly MonitoringException _case;

    public CalendarInboxTests()
    {
        _case = new MonitoringException
        {
            Id = Guid.NewGuid(), TenantId = TenantId, EmployeeId = _subject.Id, Type = ExceptionType.MeetingNoShow, Status = ExceptionStatus.Open,
            Title = "Missed meetings", Description = "3 or more in 30 days.", DetectedAt = Now, MetadataJson = "{\"source\":\"calendar\",\"severity\":\"medium\"}",
        };
        _exceptions.Setup(e => e.GetListAsync(TenantId, It.Is<ExceptionListFilter>(f => f.Calendar == true), 1, It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([_case]);
        _exceptions.Setup(e => e.GetByIdAsync(TenantId, _case.Id, It.IsAny<CancellationToken>())).ReturnsAsync(_case);
        _employees.Setup(e => e.ListByIdsAsync(TenantId, It.IsAny<IReadOnlyCollection<Guid>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Dictionary<Guid, Employee> { [_subject.Id] = _subject });
        _conversations.Setup(c => c.ListAddedSubjectsAsync(TenantId, It.IsAny<Guid>(), "calendar", It.IsAny<CancellationToken>())).ReturnsAsync([]);
        var eventId = Guid.NewGuid();
        _incidents.Setup(i => i.ListForExceptionAsync(TenantId, _case.Id, It.IsAny<CancellationToken>())).ReturnsAsync(
        [
            new CalendarMeetingIncident
            {
                Id = Guid.NewGuid(), TenantId = TenantId, EmployeeId = _subject.Id, RuleCode = CalendarRuleCodes.NoShow, EventId = eventId,
                OccurrenceStart = Now.AddDays(-1), Confidence = CalendarIncidentConfidence.Agent, ExceptionId = _case.Id,
                EvidenceJson = "{\"title\":\"Sprint review\",\"rsvp\":\"accepted\",\"agentUsageRatio\":0.12,\"apps\":[\"zoom.exe\"]}",
            },
        ]);
        _events.Setup(e => e.ListRsvpHistoryAsync(TenantId, _subject.Id, It.IsAny<IReadOnlyCollection<Guid>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([new CalendarEventParticipantRsvpHistory { EventId = eventId, FromStatus = "pending", ToStatus = "accepted", ChangedAt = Now.AddDays(-3) }]);
    }

    private void ReviewerScope(bool isHr = false)
        => _scope.Setup(s => s.ResolveAsync(true, It.IsAny<IReadOnlyCollection<Guid>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ExceptionScope(isHr, _manager, [_subject.Id]));

    private ExceptionCaseActions Actions() => new(_sender.Object, _exceptions.Object, new InMemoryInboxDecisionJournal(), _recorder.Object,
        _policies.Object, new FakeUnitOfWork(), new FakeDateTimeProvider { UtcNow = Now }, _notifications.Object);

    private CalendarInboxSourceProvider Provider() => new(_exceptions.Object, _scope.Object, _employees.Object, _incidents.Object, _events.Object,
        Actions(), _conversations.Object);

    [Fact]
    public async Task The_reviewer_receives_the_case_with_the_X1_actions_and_the_employee_sees_it_as_info()
    {
        ReviewerScope();
        var received = (await Provider().ListAsync(TenantId, new InboxQuery(), _manager, default)).Single();
        (received.Source, received.Kind, received.Direction, received.Severity).Should().Be(("calendar", "meeting_exception", InboxDirections.Received, "medium"));
        received.AvailableActions.Select(a => a.Key).Should().Equal(
            ExceptionCaseInboxActions.Resolve, ExceptionCaseInboxActions.Escalate);

        var own = (await Provider().ListAsync(TenantId, new InboxQuery(), _subject.Id, default)).Single();
        (own.Direction, own.AvailableActions.Count).Should().Be((InboxDirections.Info, 0));
    }

    [Fact]
    public async Task Outside_the_exception_scope_there_is_nothing_to_see()
    {
        (await Provider().ListAsync(TenantId, new InboxQuery(), Guid.NewGuid(), default)).Should().BeEmpty();
        (await Provider().GetDetailAsync(TenantId, "meeting_exception", _case.Id, Guid.NewGuid(), default)).Should().BeNull();
    }

    [Fact]
    public async Task An_escalation_target_outside_coverage_receives_opens_and_works_the_case_they_hold()
    {
        _scope.Setup(s => s.ResolveAsync(It.IsAny<bool>(), It.IsAny<IReadOnlyCollection<Guid>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ExceptionScope(false, _manager, [], HeldCaseIds: [_case.Id]));

        var received = (await Provider().ListAsync(TenantId, new InboxQuery(), _manager, default)).Single();
        var detail = await Provider().GetDetailAsync(TenantId, "meeting_exception", _case.Id, _manager, default);

        received.Direction.Should().Be(InboxDirections.Received);
        detail!.Item.AvailableActions.Select(a => a.Key).Should().Contain(ExceptionCaseInboxActions.Resolve);
    }

    [Fact]
    public async Task HR_sees_calendar_cases_only_as_info()
    {
        ReviewerScope(isHr: true);
        (await Provider().ListAsync(TenantId, new InboxQuery(), _manager, default)).Single().Direction.Should().Be(InboxDirections.Info);
    }

    [Fact]
    public async Task The_evidence_is_meeting_facts_only_with_an_estimated_badge_and_never_monitoring_data()
    {
        ReviewerScope();

        var detail = await Provider().GetDetailAsync(TenantId, "meeting_exception", _case.Id, _manager, default);

        var json = detail!.ExtraJson!;
        json.Should().NotContain("agentUsageRatio").And.NotContain("zoom.exe").And.NotContain("apps");
        using var doc = JsonDocument.Parse(json);
        var incident = doc.RootElement.GetProperty("incidents")[0];
        incident.GetProperty("estimated").GetBoolean().Should().BeTrue();
        incident.GetProperty("facts").GetProperty("title").GetString().Should().Be("Sprint review");
        incident.GetProperty("rsvpHistory")[0].GetProperty("to").GetString().Should().Be("accepted");
    }

    [Fact]
    public async Task Escalating_a_calendar_case_goes_to_the_managers_manager_and_never_to_HR()
    {
        ReviewerScope();
        var above = new EscalationRecipient(Guid.NewGuid(), Guid.NewGuid());
        var policy = new Mock<IInboxEscalationPolicy>();
        policy.Setup(p => p.NextAsync(TenantId, It.IsAny<EscalationContext>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new EscalationTarget.Employees([above]));
        _policies.Setup(p => p.For("calendar", nameof(ExceptionType.MeetingNoShow))).Returns(policy.Object);
        _sender.Setup(s => s.Send(It.IsAny<EscalateExceptionCommand>(), It.IsAny<CancellationToken>()))
            .Callback(() => _case.Status = ExceptionStatus.Escalated).ReturnsAsync(Result.Success());
        _recorder.Setup(r => r.RecordAsync(TenantId, It.IsAny<EscalationRecord>(), It.IsAny<CancellationToken>())).ReturnsAsync([above.EmployeeId!.Value]);

        var result = await Provider().ExecuteActionAsync(TenantId, "meeting_exception", _case.Id, ExceptionCaseInboxActions.Escalate,
            JsonSerializer.SerializeToElement(new { comment = "No reply from Pat" }), _manager, default);

        result.IsSuccess.Should().BeTrue(result.Error);
        _policies.Verify(p => p.For("monitoring", It.IsAny<string>()), Times.Never);
        _recorder.Verify(r => r.RecordAsync(TenantId, It.Is<EscalationRecord>(e => e.Item.Source == "calendar" && e.Target is EscalationTarget.Employees),
            It.IsAny<CancellationToken>()), Times.Once);
        _notifications.Verify(n => n.SendTemplatedAsync(TenantId, above.UserId, "calendar_exception_escalated",
            It.IsAny<IReadOnlyDictionary<string, string>>(), "inbox:calendar:meeting_exception", _case.Id, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task The_monitoring_alert_router_never_alerts_anyone_for_a_calendar_case()
    {
        var notifications = new Mock<INotificationDispatcher>();
        var people = new Mock<ONEVO.Application.Features.CoreHr.Employee.RepositoryInterfaces.IEmployeeRepository>();
        var router = new ONEVO.Application.Features.Monitoring.Exceptions.Services.ExceptionAlertRouter(
            Mock.Of<ONEVO.Application.Features.CoreHr.EmployeeAuthority.ServiceInterfaces.IEmployeeAuthorityResolver>(),
            people.Object,
            Mock.Of<ONEVO.Application.Features.Auth.Permission.RepositoryInterfaces.IPermissionRepository>(),
            notifications.Object, new FakeDateTimeProvider { UtcNow = Now });

        await router.NotifyDetectedAsync(_case, default);
        await router.NotifyEscalatedAsync(_case, default);

        people.Invocations.Should().BeEmpty("no reviewer is even looked up");
        notifications.Invocations.Should().BeEmpty();
    }

    private static readonly DateTimeOffset MonthStart = new(2026, 10, 1, 0, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset MonthEnd = new(2026, 11, 1, 0, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task ListPersonCases_QueriesCalendarCasesOfTheSubjectInRange()
    {
        ExceptionListFilter? used = null;
        _exceptions.Setup(e => e.GetListAsync(TenantId, It.IsAny<ExceptionListFilter>(), 1, It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .Callback((Guid _, ExceptionListFilter f, int _, int _, CancellationToken _) => used = f)
            .ReturnsAsync([_case]);
        ReviewerScope();

        var row = (await Provider().ListPersonCasesAsync(TenantId, _subject.Id, InboxCategories.Alert, MonthStart, MonthEnd, _manager, default)).Single();

        row.Item.Id.Should().Be(_case.Id);
        row.Item.Direction.Should().Be(InboxDirections.Info);
        row.Item.AvailableActions.Should().BeEmpty();
        used!.Calendar.Should().BeTrue();
        used.ActiveOnly.Should().BeFalse();
        used.EmployeeIds.Should().Equal(_subject.Id);
        (used.DetectedFrom, used.DetectedTo).Should().Be((MonthStart, MonthEnd));
    }

    [Fact]
    public async Task ListPersonCases_ExcludesMonitoringTypes()
    {
        _case.Type = ExceptionType.SustainedLowActivity;
        ReviewerScope();

        (await Provider().ListPersonCasesAsync(TenantId, _subject.Id, InboxCategories.Alert, MonthStart, MonthEnd, _manager, default))
            .Should().BeEmpty();
    }

    [Fact]
    public async Task ListPersonCases_CanOpenTrueInScope_FalseOutOfScope()
    {
        _exceptions.Setup(e => e.GetListAsync(TenantId, It.IsAny<ExceptionListFilter>(), 1, It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([_case]);
        ReviewerScope();
        var inScope = await Provider().ListPersonCasesAsync(TenantId, _subject.Id, InboxCategories.Alert, MonthStart, MonthEnd, _manager, default);
        _scope.Setup(s => s.ResolveAsync(true, It.IsAny<IReadOnlyCollection<Guid>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((ExceptionScope?)null);
        var outOfScope = await Provider().ListPersonCasesAsync(TenantId, _subject.Id, InboxCategories.Alert, MonthStart, MonthEnd, Guid.NewGuid(), default);

        inScope.Single().CanOpen.Should().BeTrue();
        outOfScope.Single().CanOpen.Should().BeFalse();
    }
}
