using System.Text.Json;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using ONEVO.Application.Common.RepositoryInterfaces;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.Calendar.Exceptions;
using ONEVO.Application.Features.Calendar.Inbox;
using ONEVO.Application.Features.Calendar.OutboxHandlers;
using ONEVO.Application.Features.Calendar.RepositoryInterfaces;
using ONEVO.Application.Features.Calendar.ServiceInterfaces;
using ONEVO.Application.Features.Conversations.Contracts;
using ONEVO.Application.Features.CoreHr.EmployeeAuthority.Services;
using ONEVO.Application.Features.Dashboard.Team.DTOs;
using ONEVO.Application.Features.Dashboard.Team.Sources;
using ONEVO.Application.Features.DevPlatform.Tenancy.RepositoryInterfaces;
using ONEVO.Application.Features.Inbox.Contracts;
using ONEVO.Application.Features.Inbox.Escalation;
using ONEVO.Application.Features.Leave.Request.RepositoryInterfaces;
using ONEVO.Application.Features.Monitoring.Exceptions.Inbox;
using ONEVO.Application.Features.Monitoring.Exceptions.RepositoryInterfaces;
using ONEVO.Application.Features.Monitoring.Exceptions.ServiceInterfaces;
using ONEVO.Application.Features.Monitoring.Meetings.RepositoryInterfaces;
using ONEVO.Application.Features.OrgStructure.RepositoryInterfaces;
using ONEVO.Application.Features.WorkManagement.Common.Services;
using ONEVO.Domain.Features.CoreHr.Entities;
using ONEVO.Domain.Features.InfrastructureModule.Entities;
using ONEVO.Domain.Features.Monitoring.Exceptions.Entities;
using ONEVO.Infrastructure.Identity.Tenancy;
using ONEVO.Infrastructure.Services.Calendar;
using ONEVO.Tests.Unit.Fakes;
using ONEVO.Tests.Unit.Features.Inbox;
using Xunit;
using CoreEmployeeRepository = ONEVO.Application.Features.CoreHr.Employee.RepositoryInterfaces.IEmployeeRepository;
using MonitoringException = ONEVO.Domain.Features.Monitoring.Exceptions.Entities.Exception;
using TaskMeetingRepository = ONEVO.Application.Features.WorkManagement.Meetings.RepositoryInterfaces.IMeetingRepository;

namespace ONEVO.Tests.Unit.Features.Calendar.Exceptions;

/// <summary>Plan BZ coverage audit: the calendar meeting-exception pieces C2/C3 shipped without a test of their own.</summary>
public sealed class CalendarCoverageAuditTests
{
    private static readonly Guid TenantId = Guid.NewGuid();
    private static readonly DateTimeOffset Now = new(2026, 10, 14, 10, 0, 0, TimeSpan.Zero);
    private readonly Guid _subject = Guid.NewGuid();
    private readonly Guid _reviewer = Guid.NewGuid();
    private readonly Mock<IExceptionRepository> _exceptions = new();
    private readonly Mock<IExceptionScopeResolver> _scope = new();
    private readonly MonitoringException _case;

    public CalendarCoverageAuditTests()
    {
        _case = new MonitoringException
        {
            Id = Guid.NewGuid(), TenantId = TenantId, EmployeeId = _subject, Type = ExceptionType.MeetingNoShow, Status = ExceptionStatus.Open,
            Title = "Missed meetings", Description = "3 or more in 30 days.", DetectedAt = Now.AddDays(-1),
        };
        _exceptions.Setup(e => e.GetByIdAsync(TenantId, _case.Id, It.IsAny<CancellationToken>())).ReturnsAsync(_case);
    }

    private ItemRef Item => new(CalendarInboxKinds.Source, CalendarInboxKinds.MeetingException, _case.Id);

    // ---- engine with mocked inputs: an employee is "evaluated" when the engine looks them up -------------------------

    private static CalendarIncidentEngine Engine(Mock<CoreEmployeeRepository> employees)
    {
        var cases = new CalendarCaseService(Mock.Of<ICalendarIncidentRepository>(), Mock.Of<IExceptionRepository>(),
            Mock.Of<IInboxEscalationPolicyRegistry>(), Mock.Of<INotificationDispatcher>(), employees.Object);
        return new CalendarIncidentEngine(Mock.Of<ICalendarEventRepository>(), Mock.Of<ICalendarEventMeetingRepository>(),
            Mock.Of<ICalendarEventMeetingAttendanceRepository>(), Mock.Of<ICalendarRecurrenceExpander>(), Mock.Of<ILeaveRequestReadRepository>(),
            Mock.Of<ILegalEntityRepository>(), employees.Object, Mock.Of<IExternalCalendarConnectionRepository>(),
            Mock.Of<IMeetingComplianceReadRepository>(), Mock.Of<IEmployeeAuthorityResolverFactory>(), [], cases,
            new FakeDateTimeProvider { UtcNow = Now });
    }

    [Fact]
    public async Task The_evaluate_outbox_handler_evaluates_every_distinct_employee_even_after_one_fails_and_then_rethrows_for_a_retry()
    {
        Guid failing = Guid.NewGuid(), healthy = Guid.NewGuid();
        var employees = new Mock<CoreEmployeeRepository>();
        employees.Setup(e => e.GetByIdAsync(TenantId, failing, It.IsAny<CancellationToken>())).ThrowsAsync(new InvalidOperationException("db"));
        employees.Setup(e => e.GetByIdAsync(TenantId, healthy, It.IsAny<CancellationToken>())).ReturnsAsync((Employee?)null);
        var handler = new CalendarIncidentEvaluateOutboxHandler(Engine(employees), NullLogger<CalendarIncidentEvaluateOutboxHandler>.Instance);

        var act = () => handler.HandleAsync(JsonSerializer.Serialize(new CalendarIncidentEvaluatePayload(TenantId, [failing, healthy, healthy])), default);

        handler.Type.Should().Be(OutboxMessageTypes.CalendarIncidentEvaluate);
        await act.Should().ThrowAsync<InvalidOperationException>().WithInnerException(typeof(InvalidOperationException));
        employees.Verify(e => e.GetByIdAsync(TenantId, healthy, It.IsAny<CancellationToken>()), Times.Once, "evaluated once, after the failure");
    }

    [Fact]
    public async Task The_evaluate_outbox_handler_refuses_an_empty_payload()
    {
        var handler = new CalendarIncidentEvaluateOutboxHandler(Engine(new Mock<CoreEmployeeRepository>()), NullLogger<CalendarIncidentEvaluateOutboxHandler>.Instance);

        await handler.Invoking(h => h.HandleAsync("null", default)).Should().ThrowAsync<InvalidOperationException>();
    }

    [Fact]
    public async Task The_nightly_job_evaluates_each_tenants_invitees_purges_old_unattached_incidents_and_skips_a_failing_tenant()
    {
        Guid failing = Guid.NewGuid(), healthy = Guid.NewGuid(), brokenEmployee = Guid.NewGuid(), fineEmployee = Guid.NewGuid();
        var tenants = new Mock<ITenantRepository>();
        tenants.Setup(t => t.ListAsync(TenantStatus.Active, null, 0, int.MaxValue, It.IsAny<CancellationToken>())).ReturnsAsync(
        [
            new Tenant { Id = failing, Name = "A", Slug = "a", Status = TenantStatus.Active },
            new Tenant { Id = healthy, Name = "B", Slug = "b", Status = TenantStatus.Active },
        ]);
        var events = new Mock<ICalendarEventRepository>();
        events.Setup(e => e.ListParticipantEmployeeIdsInRangeAsync(failing, It.IsAny<DateTimeOffset>(), It.IsAny<DateTimeOffset>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("bad tenant"));
        events.Setup(e => e.ListParticipantEmployeeIdsInRangeAsync(healthy, Now.AddDays(-CalendarExceptionDefaults.LongWindowDays),
                Now.AddDays(CalendarIncidentEngine.LookAheadDays), It.IsAny<CancellationToken>()))
            .ReturnsAsync([brokenEmployee, fineEmployee]);
        // A series participant has no calendar event row: task-meeting attendees come from the meetings side. Someone in
        // both lists is evaluated once.
        var taskMeetingOnly = Guid.NewGuid();
        var meetings = new Mock<TaskMeetingRepository>();
        meetings.Setup(m => m.ListAttendeeEmployeeIdsInRangeAsync(healthy, Now.AddDays(-CalendarExceptionDefaults.LongWindowDays),
                Now.AddDays(CalendarIncidentEngine.LookAheadDays), It.IsAny<CancellationToken>()))
            .ReturnsAsync([fineEmployee, taskMeetingOnly]);
        var employees = new Mock<CoreEmployeeRepository>();
        employees.Setup(e => e.GetByIdAsync(healthy, brokenEmployee, It.IsAny<CancellationToken>())).ThrowsAsync(new InvalidOperationException("row"));
        employees.Setup(e => e.GetByIdAsync(healthy, fineEmployee, It.IsAny<CancellationToken>())).ReturnsAsync((Employee?)null);
        employees.Setup(e => e.GetByIdAsync(healthy, taskMeetingOnly, It.IsAny<CancellationToken>())).ReturnsAsync((Employee?)null);
        var incidents = new Mock<ICalendarIncidentRepository>();
        incidents.Setup(i => i.PurgeUnattachedAsync(healthy, It.IsAny<DateTimeOffset>(), It.IsAny<CancellationToken>())).ReturnsAsync(2);
        var uow = new Mock<IUnitOfWork>();

        var services = new ServiceCollection();
        services.AddSingleton(tenants.Object);
        services.AddSingleton(Mock.Of<ITenantContextSwitcher>());
        services.AddSingleton<IWritableTenantContext>(new TenantContextAccessor());
        services.AddSingleton(events.Object);
        services.AddSingleton(meetings.Object);
        services.AddSingleton(Engine(employees));
        services.AddSingleton(incidents.Object);
        services.AddSingleton(uow.Object);
        services.AddSingleton<IDateTimeProvider>(new FakeDateTimeProvider { UtcNow = Now });

        var opened = await new CalendarIncidentJob(services.BuildServiceProvider(), NullLogger<CalendarIncidentJob>.Instance).RunOnceAsync(default);

        opened.Should().Be(0);
        employees.Verify(e => e.GetByIdAsync(healthy, fineEmployee, It.IsAny<CancellationToken>()), Times.Once, "one employee failing never stops the rest");
        employees.Verify(e => e.GetByIdAsync(healthy, taskMeetingOnly, It.IsAny<CancellationToken>()), Times.Once,
            "an employee whose only meetings are task meetings is evaluated too");
        incidents.Verify(i => i.PurgeUnattachedAsync(healthy, Now.AddDays(-CalendarExceptionDefaults.IncidentRetentionDays), It.IsAny<CancellationToken>()), Times.Once);
        incidents.Verify(i => i.PurgeUnattachedAsync(failing, It.IsAny<DateTimeOffset>(), It.IsAny<CancellationToken>()), Times.Never);
        incidents.Verify(i => i.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
        uow.Verify(u => u.ClearTracking(), Times.Exactly(2), "once for the employee, once for the tenant");
    }

    // ---- conversation subject resolver ------------------------------------------------------------------------------

    private CalendarConversationSubjectResolver Resolver(Guid callerEmployeeId, IInboxEscalationPolicyRegistry? policies = null)
    {
        var identity = new Mock<ICallerIdentityResolver>();
        identity.Setup(i => i.ResolveCallerEmployeeIdAsync(TenantId, It.IsAny<Guid>(), It.IsAny<CancellationToken>())).ReturnsAsync(callerEmployeeId);
        return new CalendarConversationSubjectResolver(_exceptions.Object, _scope.Object, policies ?? Mock.Of<IInboxEscalationPolicyRegistry>(),
            Mock.Of<ICurrentUser>(), identity.Object);
    }

    [Fact]
    public async Task The_chat_on_a_calendar_case_starts_with_the_employee_and_the_first_reviewers_of_its_policy()
    {
        var policy = new Mock<IInboxEscalationPolicy>();
        policy.Setup(p => p.InitialAsync(TenantId, It.IsAny<EscalationContext>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new EscalationTarget.Employees([new EscalationRecipient(Guid.NewGuid(), _reviewer), new EscalationRecipient(Guid.NewGuid(), null)]));
        var policies = new Mock<IInboxEscalationPolicyRegistry>();
        policies.Setup(p => p.For("calendar", nameof(ExceptionType.MeetingNoShow))).Returns(policy.Object);

        var subject = await Resolver(_reviewer, policies.Object).ResolveAsync(TenantId, Item, default);

        subject!.BaseParticipantEmployeeIds.Should().Equal(_subject, _reviewer);
        (subject.Title, subject.SubjectEmployeeId).Should().Be(("Missed meetings", _subject));
    }

    [Fact]
    public async Task A_monitoring_case_or_another_kind_is_not_a_calendar_conversation_subject()
    {
        var resolver = Resolver(_reviewer);
        (await resolver.ResolveAsync(TenantId, Item with { Kind = "other" }, default)).Should().BeNull();

        _case.Type = ExceptionType.SustainedLowActivity;
        (await resolver.ResolveAsync(TenantId, Item, default)).Should().BeNull();
        (await resolver.CanViewAsync(TenantId, Item, _reviewer, default)).Should().BeFalse();
    }

    [Fact]
    public async Task Viewing_needs_the_view_scope_reviewing_needs_the_action_scope_and_nobody_can_ask_on_someone_elses_behalf()
    {
        _scope.Setup(s => s.ResolveAsync(false, It.IsAny<IReadOnlyCollection<Guid>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ExceptionScope(false, _reviewer, [_subject]));
        _scope.Setup(s => s.ResolveAsync(true, It.IsAny<IReadOnlyCollection<Guid>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((ExceptionScope?)null);

        (await Resolver(_reviewer).CanViewAsync(TenantId, Item, _reviewer, default)).Should().BeTrue();
        (await Resolver(_reviewer).CanReviewAsync(TenantId, Item, _reviewer, default)).Should().BeFalse();
        (await Resolver(Guid.NewGuid()).CanViewAsync(TenantId, Item, _reviewer, default)).Should().BeFalse("the caller is not that employee");
    }

    // ---- reverter -----------------------------------------------------------------------------------------------------

    [Fact]
    public async Task Undoing_a_calendar_case_decision_restores_its_previous_state_under_the_calendar_source()
    {
        _case.Status = ExceptionStatus.Resolved;
        _case.Outcome = ExceptionOutcomes.Confirmed;
        var journal = new InMemoryInboxDecisionJournal();
        journal.Add(TenantId, Item, "resolve:confirmed", _reviewer, Now.AddMinutes(-2))
            .BeforeJson = new ExceptionStateSnapshot("t", "Open", null, null, null, null, null, null, null, null).ToJson();
        _scope.Setup(s => s.ResolveAsync(It.IsAny<bool>(), It.IsAny<IReadOnlyCollection<Guid>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ExceptionScope(false, _reviewer, [_subject]));
        var reverter = new CalendarMeetingExceptionReverter(journal, new FakeUnitOfWork(), new FakeDateTimeProvider { UtcNow = Now }, _exceptions.Object, _scope.Object);

        var result = await reverter.RevertAsync(TenantId, Item, _reviewer, null, default);

        (reverter.Source, reverter.Kind).Should().Be(("calendar", "meeting_exception"));
        result.IsSuccess.Should().BeTrue(result.Error);
        (_case.Status, _case.Outcome).Should().Be((ExceptionStatus.Open, (string?)null)!);
    }

    [Fact]
    public async Task A_calendar_case_decision_cannot_be_undone_once_the_case_left_the_callers_scope()
    {
        _case.Status = ExceptionStatus.Acknowledged;
        var journal = new InMemoryInboxDecisionJournal();
        journal.Add(TenantId, Item, "acknowledge", _reviewer, Now.AddMinutes(-2))
            .BeforeJson = new ExceptionStateSnapshot("t", "Open", null, null, null, null, null, null, null, null).ToJson();
        _scope.Setup(s => s.ResolveAsync(It.IsAny<bool>(), It.IsAny<IReadOnlyCollection<Guid>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((ExceptionScope?)null);

        var result = await new CalendarMeetingExceptionReverter(journal, new FakeUnitOfWork(), new FakeDateTimeProvider { UtcNow = Now }, _exceptions.Object, _scope.Object)
            .RevertAsync(TenantId, Item, _reviewer, null, default);

        result.IsSuccess.Should().BeFalse();
        _case.Status.Should().Be(ExceptionStatus.Acknowledged);
    }

    // ---- My Team action source ------------------------------------------------------------------------------------------

    private CalendarMeetingExceptionTeamActionSource TeamSource(ExceptionScope? scope)
    {
        var currentUser = new Mock<ICurrentUser>();
        currentUser.SetupGet(c => c.TenantId).Returns(TenantId);
        _exceptions.Setup(e => e.ListEmployeeIdsWithExceptionsAsync(TenantId, It.IsAny<CancellationToken>())).ReturnsAsync([_subject]);
        _scope.Setup(s => s.ResolveAsync(false, It.IsAny<IReadOnlyCollection<Guid>>(), It.IsAny<CancellationToken>())).ReturnsAsync(scope);
        var employees = new Mock<ONEVO.Application.Features.CoreHr.Employee.RepositoryInterfaces.IEmployeeRepository>();
        employees.Setup(e => e.ListByIdsAsync(TenantId, It.IsAny<IReadOnlyCollection<Guid>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Dictionary<Guid, Employee> { [_subject] = new() { Id = _subject, FirstName = "Pat", LastName = "Doe" } });
        return new CalendarMeetingExceptionTeamActionSource(currentUser.Object, _scope.Object, _exceptions.Object, employees.Object);
    }

    [Fact]
    public async Task My_Team_lists_only_calendar_cases_and_links_them_to_the_calendar_meeting_exception_item()
    {
        _exceptions.Setup(e => e.GetListAsync(TenantId, It.Is<ExceptionListFilter>(f => f.Calendar == true && f.Statuses != null), 1, int.MaxValue, It.IsAny<CancellationToken>()))
            .ReturnsAsync([_case]);
        _exceptions.Setup(e => e.GetListTotalCountAsync(TenantId, It.Is<ExceptionListFilter>(f => f.Calendar == true && f.Status == ExceptionStatus.Acknowledged), It.IsAny<CancellationToken>()))
            .ReturnsAsync(1);
        var source = TeamSource(new ExceptionScope(false, _reviewer, [_subject]));

        var summary = await source.GetSummaryAsync(Guid.NewGuid(), 5, default);

        (await source.IsGatedAsync(default)).Should().BeTrue();
        (summary.PendingCount, summary.InProgressCount, summary.OldestPendingAt).Should().Be((1, 1, _case.DetectedAt));
        var item = summary.TopItems.Single();
        (item.EntityId, item.SubjectName, item.Link!.Kind).Should().Be((_case.Id, "Pat Doe", ActionItemLink.KindCalendarMeetingException));
        item.Link.Params["exceptionId"].Should().Be(_case.Id.ToString());
        _exceptions.Verify(e => e.GetListAsync(TenantId, It.Is<ExceptionListFilter>(f => f.Calendar != true), It.IsAny<int>(), It.IsAny<int>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Without_an_exception_scope_My_Team_shows_no_calendar_cases()
    {
        var source = TeamSource(null);

        (await source.IsGatedAsync(default)).Should().BeFalse();
        var summary = await source.GetSummaryAsync(Guid.NewGuid(), 5, default);
        (summary.PendingCount, summary.TopItems.Count).Should().Be((0, 0));
        _exceptions.Verify(e => e.GetListAsync(It.IsAny<Guid>(), It.IsAny<ExceptionListFilter>(), It.IsAny<int>(), It.IsAny<int>(), It.IsAny<CancellationToken>()), Times.Never);
    }
}
