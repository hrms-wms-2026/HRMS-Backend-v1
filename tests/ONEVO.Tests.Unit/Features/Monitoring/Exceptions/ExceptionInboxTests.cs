using System.Text.Json;
using FluentAssertions;
using MediatR;
using Moq;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Common.RepositoryInterfaces;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.Conversations.Contracts;
using ONEVO.Application.Features.Conversations.RepositoryInterfaces;
using ONEVO.Application.Features.CoreHr.EmployeeAuthority.Models;
using ONEVO.Application.Features.CoreHr.EmployeeAuthority.ServiceInterfaces;
using ONEVO.Application.Features.Inbox.Contracts;
using ONEVO.Application.Features.Inbox.Escalation;
using ONEVO.Application.Features.Monitoring.ActivityMonitoring.ServiceInterfaces;
using ONEVO.Application.Features.Monitoring.CheckIn.RepositoryInterfaces;
using ONEVO.Application.Features.Monitoring.Exceptions.Commands.AcknowledgeException;
using ONEVO.Application.Features.Monitoring.Exceptions.Commands.EscalateException;
using ONEVO.Application.Features.Monitoring.Exceptions.Commands.ResolveException;
using ONEVO.Application.Features.Monitoring.Exceptions.Inbox;
using ONEVO.Application.Features.Monitoring.Exceptions.Queries.GetExceptionEvidence;
using ONEVO.Application.Features.Monitoring.Exceptions.RepositoryInterfaces;
using ONEVO.Application.Features.Monitoring.Exceptions.ServiceInterfaces;
using ONEVO.Application.Features.Monitoring.Exceptions.Services;
using ONEVO.Application.Features.TimeAttendance.RepositoryInterfaces;
using ONEVO.Domain.Features.CoreHr.Entities;
using ONEVO.Domain.Features.Inbox.Entities;
using ONEVO.Domain.Features.Monitoring.CheckIn.Entities;
using ONEVO.Domain.Features.Monitoring.Exceptions.Entities;
using ONEVO.Domain.Features.TimeAttendance.Entities;
using ONEVO.Tests.Unit.Fakes;
using ONEVO.Tests.Unit.Features.Inbox;
using Xunit;
using MonitoringException = ONEVO.Domain.Features.Monitoring.Exceptions.Entities.Exception;

namespace ONEVO.Tests.Unit.Features.Monitoring.Exceptions;

/// <summary>Monitoring exception cases in the Inbox (plan X1) and the camera-skipped case (D-b).</summary>
public class ExceptionInboxTests
{
    private static readonly Guid TenantId = Guid.NewGuid();
    private static readonly Guid Reviewer = Guid.NewGuid();
    private static readonly Guid Subject = Guid.NewGuid();
    private static readonly Guid Stranger = Guid.NewGuid();
    private static readonly Guid LegalEntity = Guid.NewGuid();
    private static readonly DateTimeOffset Now = new(2026, 10, 14, 10, 0, 0, TimeSpan.Zero);

    private readonly Mock<IExceptionRepository> _exceptions = new();
    private readonly Mock<IExceptionScopeResolver> _scope = new();
    private readonly Mock<IEmployeeRepository> _employees = new();
    private readonly Mock<ISender> _sender = new();
    private readonly Mock<IConversationRepository> _conversations = new();
    private readonly Mock<IEmployeeAuthorityResolver> _authority = new();
    private readonly InMemoryInboxDecisionJournal _journal = new();
    private readonly Mock<IInboxEscalationRecorder> _recorder = new();
    private readonly Mock<IInboxEscalationPolicy> _policy = new();
    private readonly List<MonitoringException> _cases = new();

    public ExceptionInboxTests()
    {
        _exceptions.Setup(x => x.GetListAsync(TenantId, It.IsAny<ExceptionListFilter>(), 1, It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(() => _cases.ToList());
        _exceptions.Setup(x => x.GetByIdAsync(TenantId, It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Guid _, Guid id, CancellationToken _) => _cases.FirstOrDefault(c => c.Id == id));
        _employees.Setup(x => x.ListByIdsAsync(TenantId, It.IsAny<IReadOnlyCollection<Guid>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Dictionary<Guid, Employee> { [Subject] = new() { Id = Subject, FirstName = "Sam", LegalEntityId = LegalEntity } });
        ApproverOf();
        _authority.Setup(a => a.ResolveApproverAsync(It.IsAny<EmployeeApprovalRouteRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<EmployeeApprovalRoute>.Success(new EmployeeApprovalRoute(Stranger, Guid.NewGuid(), Guid.NewGuid(),
                "attendance:approve", EmployeeAuthorityPurpose.ExceptionAlertReview, EmployeeApprovalRouteSource.ReportingLine, null)));
        _conversations.Setup(x => x.ListAddedSubjectsAsync(TenantId, It.IsAny<Guid>(), "monitoring", It.IsAny<CancellationToken>())).ReturnsAsync([]);
        _policy.Setup(p => p.AppliesTo("monitoring", It.IsAny<string>())).Returns(true);
    }

    private MonitoringException Case(ExceptionStatus status = ExceptionStatus.Open)
    {
        var c = new MonitoringException
        {
            Id = Guid.NewGuid(), TenantId = TenantId, EmployeeId = Subject, Type = ExceptionType.SustainedLowActivity, Status = status,
            Title = "Sustained low activity", Description = "Score below 30 for 3 days.", DetectedAt = Now.AddDays(-1),
        };
        _cases.Add(c);
        return c;
    }

    private void ScopeIs(bool isHr, params Guid[] employees)
        => _scope.Setup(s => s.ResolveAsync(It.IsAny<bool>(), It.IsAny<IReadOnlyCollection<Guid>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ExceptionScope(isHr, Reviewer, employees));

    /// <summary>The employees the caller is the exact attendance approver of.</summary>
    private void ApproverOf(params Guid[] employees)
        => _authority.Setup(a => a.ResolveApprovalInboxScopeAsync(It.IsAny<EmployeeApprovalInboxScopeRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((EmployeeApprovalInboxScopeRequest r, CancellationToken _) => r.CandidateEmployeeIds.Where(employees.Contains).ToList());

    private ExceptionCaseActions Actions() => new(_sender.Object, _exceptions.Object, _journal, _recorder.Object,
        new InboxEscalationPolicyRegistry([_policy.Object]), new FakeUnitOfWork(), new FakeDateTimeProvider { UtcNow = Now });

    private ExceptionInboxSourceProvider Provider()
        => new(_exceptions.Object, _scope.Object, _employees.Object, _sender.Object, Actions(), _conversations.Object,
            new ExceptionCaseRouting(_employees.Object, _authority.Object, _journal));

    [Fact]
    public async Task A_manager_receives_the_cases_they_are_the_approver_for_and_someone_without_scope_sees_nothing()
    {
        var mine = Case();
        ScopeIs(isHr: false, Subject);
        ApproverOf(Subject);

        var asManager = await Provider().ListAsync(TenantId, new InboxQuery(), Reviewer, default);
        _scope.Setup(s => s.ResolveAsync(It.IsAny<bool>(), It.IsAny<IReadOnlyCollection<Guid>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((ExceptionScope?)null);
        var asStranger = await Provider().ListAsync(TenantId, new InboxQuery(), Stranger, default);

        asManager.Select(i => (i.Id, i.Direction)).Should().Equal((mine.Id, InboxDirections.Received));
        asManager[0].AvailableActions.Select(a => a.Key).Should().Equal("acknowledge", "resolve", "escalate");
        asStranger.Should().BeEmpty();
    }

    [Fact]
    public async Task Coverage_alone_does_not_put_a_case_in_the_inbox_of_someone_above()
    {
        Case();
        ScopeIs(isHr: false, Subject);

        var items = await Provider().ListAsync(TenantId, new InboxQuery(), Reviewer, default);

        items.Should().BeEmpty();
    }

    [Fact]
    public async Task HR_only_receives_cases_nobody_else_can_review_not_every_case()
    {
        var routine = Case();
        var escalatedBeforeTheJournal = Case(ExceptionStatus.Escalated);
        ScopeIs(isHr: true);

        var items = await Provider().ListAsync(TenantId, new InboxQuery(), Reviewer, default);

        items.Should().ContainSingle(i => i.Id == escalatedBeforeTheJournal.Id && i.Direction == InboxDirections.Received);
        items.Should().NotContain(i => i.Id == routine.Id);
    }

    [Fact]
    public async Task HR_receives_the_cases_of_employees_with_no_attendance_approver()
    {
        var orphan = Case();
        ScopeIs(isHr: true);
        _authority.Setup(a => a.ResolveApproverAsync(It.IsAny<EmployeeApprovalRouteRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<EmployeeApprovalRoute>.UnprocessableEntity("No eligible approver was found for this employee and action."));

        var items = await Provider().ListAsync(TenantId, new InboxQuery(), Reviewer, default);

        items.Should().ContainSingle(i => i.Id == orphan.Id && i.Direction == InboxDirections.Received);
    }

    [Fact]
    public async Task Whoever_a_case_was_escalated_to_receives_it()
    {
        var held = Case(ExceptionStatus.Escalated);
        _scope.Setup(s => s.ResolveAsync(It.IsAny<bool>(), It.IsAny<IReadOnlyCollection<Guid>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ExceptionScope(false, Reviewer, [], [held.Id]));

        var items = await Provider().ListAsync(TenantId, new InboxQuery(), Reviewer, default);

        items.Should().ContainSingle(i => i.Id == held.Id && i.Direction == InboxDirections.Received && i.AvailableActions.Count > 0);
    }

    [Fact]
    public async Task The_employee_a_case_is_about_sees_facts_only_and_never_the_monitoring_evidence()
    {
        var mine = Case();
        ScopeIs(isHr: false);

        var listed = await Provider().ListAsync(TenantId, new InboxQuery(), Subject, default);
        var detail = await Provider().GetDetailAsync(TenantId, "exception_case", mine.Id, Subject, default);

        listed.Should().ContainSingle(i => i.Id == mine.Id && i.Direction == InboxDirections.Info && i.AvailableActions.Count == 0);
        detail!.ExtraJson.Should().BeNull();
        detail.Explanation.Should().Be(mine.Description);
        _sender.Verify(s => s.Send(It.IsAny<GetExceptionEvidenceQuery>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Resolve_stores_the_outcome_and_journals_it()
    {
        var c = Case();
        _sender.Setup(s => s.Send(It.Is<ResolveExceptionCommand>(r => r.ExceptionId == c.Id && r.Outcome == "excused"), It.IsAny<CancellationToken>()))
            .Callback(() => { c.Status = ExceptionStatus.Resolved; c.Outcome = "excused"; c.ResolvedAt = Now; })
            .ReturnsAsync(Result.Success());

        var result = await Actions().RunAsync(TenantId, new ItemRef("monitoring", "exception_case", c.Id), "resolve",
            JsonSerializer.SerializeToElement(new { outcome = "excused", comment = "client meeting" }), Reviewer, default);

        result.IsSuccess.Should().BeTrue(result.Error);
        var row = _journal.Entries.Should().ContainSingle().Subject;
        row.Action.Should().Be("resolve:excused");
        ExceptionStateSnapshot.Parse(row.BeforeJson)!.Status.Should().Be("Open");
    }

    [Fact]
    public async Task A_reviewer_replying_on_an_open_case_acknowledges_it()
    {
        var c = Case();
        _sender.Setup(s => s.Send(It.Is<AcknowledgeExceptionCommand>(a => a.ExceptionId == c.Id), It.IsAny<CancellationToken>()))
            .Callback(() => { c.Status = ExceptionStatus.Acknowledged; c.AcknowledgedAt = Now; })
            .ReturnsAsync(Result.Success());

        var affected = await new ExceptionCaseReplyAcknowledger(_exceptions.Object, Actions())
            .OnReplyAsync(TenantId, new ItemRef("monitoring", "exception_case", c.Id), Reviewer, default);

        affected.Should().BeEquivalentTo([Subject, Reviewer]);
        _journal.Entries.Should().ContainSingle().Which.Action.Should().Be("acknowledge");
    }

    [Theory]
    [InlineData(ExceptionStatus.Acknowledged, false)]
    [InlineData(ExceptionStatus.Escalated, false)]
    [InlineData(ExceptionStatus.Open, true)]
    public async Task A_reply_leaves_the_case_alone_when_it_is_not_open_or_comes_from_the_employee_it_is_about(
        ExceptionStatus status, bool fromSubject)
    {
        var c = Case(status);

        var affected = await new ExceptionCaseReplyAcknowledger(_exceptions.Object, Actions())
            .OnReplyAsync(TenantId, new ItemRef("monitoring", "exception_case", c.Id), fromSubject ? Subject : Reviewer, default);

        affected.Should().BeNull();
        _sender.Invocations.Should().BeEmpty();
    }

    [Fact]
    public async Task A_reply_from_someone_the_module_refuses_stays_just_a_message()
    {
        var c = Case();
        _sender.Setup(s => s.Send(It.IsAny<AcknowledgeExceptionCommand>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.NotFound("Exception not found."));

        var affected = await new ExceptionCaseReplyAcknowledger(_exceptions.Object, Actions())
            .OnReplyAsync(TenantId, new ItemRef("monitoring", "exception_case", c.Id), Stranger, default);

        affected.Should().BeNull();
        c.Status.Should().Be(ExceptionStatus.Open);
        _journal.Entries.Should().BeEmpty();
    }

    [Fact]
    public async Task An_invalid_outcome_is_refused_before_the_module_runs()
    {
        var c = Case();

        var result = await Actions().RunAsync(TenantId, new ItemRef("monitoring", "exception_case", c.Id), "resolve",
            JsonSerializer.SerializeToElement(new { outcome = "maybe" }), Reviewer, default);

        result.StatusCode.Should().Be(422);
        _sender.Invocations.Should().BeEmpty();
    }

    [Fact]
    public async Task Escalate_runs_the_module_command_and_records_it_with_the_cases_policy_target()
    {
        var c = Case();
        var hr = Guid.NewGuid();
        _sender.Setup(s => s.Send(It.IsAny<EscalateExceptionCommand>(), It.IsAny<CancellationToken>()))
            .Callback(() => c.Status = ExceptionStatus.Escalated).ReturnsAsync(Result.Success());
        _policy.Setup(p => p.NextAsync(TenantId, It.Is<EscalationContext>(x => x.SubjectEmployeeId == Subject), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new EscalationTarget.Employees([new EscalationRecipient(Guid.NewGuid(), hr)]));
        _recorder.Setup(r => r.RecordAsync(TenantId, It.IsAny<EscalationRecord>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([Reviewer, hr]);

        var result = await Actions().RunAsync(TenantId, new ItemRef("monitoring", "exception_case", c.Id), "escalate",
            JsonSerializer.SerializeToElement(new { comment = "please check" }), Reviewer, default);

        result.IsSuccess.Should().BeTrue(result.Error);
        result.Value!.AffectedEmployeeIds.Should().BeEquivalentTo([Reviewer, hr, Subject]);
        _recorder.Verify(r => r.RecordAsync(TenantId, It.Is<EscalationRecord>(x =>
            x.ActorEmployeeId == Reviewer && x.Note == "please check" && x.BeforeJson!.Contains("\"Open\"")
            && ((EscalationTarget.Employees)x.Target).Recipients.Single().EmployeeId == hr), It.IsAny<CancellationToken>()), Times.Once);
        _journal.Entries.Should().BeEmpty("the recorder writes the escalation's journal row");
    }

    [Fact]
    public async Task Undoing_restores_the_previous_review_state_while_the_case_is_still_in_scope()
    {
        var c = Case(ExceptionStatus.Resolved);
        c.Outcome = "confirmed";
        c.ResolvedAt = Now.AddMinutes(-2);
        _journal.Add(TenantId, new ItemRef("monitoring", "exception_case", c.Id), "resolve:confirmed", Reviewer, Now.AddMinutes(-2))
            .BeforeJson = new ExceptionStateSnapshot("t", "Acknowledged", Now.AddHours(-1), Guid.NewGuid(), null, null, null, null, null, null).ToJson();
        ScopeIs(isHr: false, Subject);

        var result = await new ExceptionCaseReverter(_journal, new FakeUnitOfWork(), new FakeDateTimeProvider { UtcNow = Now }, _exceptions.Object, _scope.Object)
            .RevertAsync(TenantId, new ItemRef("monitoring", "exception_case", c.Id), Reviewer, null, default);

        result.IsSuccess.Should().BeTrue(result.Error);
        (c.Status, c.Outcome, c.ResolvedAt).Should().Be((ExceptionStatus.Acknowledged, (string?)null, (DateTimeOffset?)null));
        _exceptions.Verify(x => x.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task A_decision_cannot_be_undone_after_the_case_was_changed_outside_the_inbox()
    {
        var c = Case(ExceptionStatus.Escalated); // escalated on the old Alerts page after the inbox resolve
        _journal.Add(TenantId, new ItemRef("monitoring", "exception_case", c.Id), "resolve:confirmed", Reviewer, Now.AddMinutes(-2),
                new ExceptionStateSnapshot("t", "Resolved", null, null, Now.AddMinutes(-2), null, null, null, null, "confirmed").ToJson())
            .BeforeJson = new ExceptionStateSnapshot("t", "Open", null, null, null, null, null, null, null, null).ToJson();
        ScopeIs(isHr: false, Subject);

        var result = await new ExceptionCaseReverter(_journal, new FakeUnitOfWork(), new FakeDateTimeProvider { UtcNow = Now }, _exceptions.Object, _scope.Object)
            .RevertAsync(TenantId, new ItemRef("monitoring", "exception_case", c.Id), Reviewer, null, default);

        result.StatusCode.Should().Be(409);
        c.Status.Should().Be(ExceptionStatus.Escalated);
    }

    [Fact]
    public async Task Taking_the_investigation_export_needs_the_action_scope_not_just_view()
    {
        var c = Case();
        var currentUser = new Mock<ICurrentUser>();
        var identity = new Mock<ONEVO.Application.Features.WorkManagement.Common.Services.ICallerIdentityResolver>();
        identity.Setup(i => i.ResolveCallerEmployeeIdAsync(TenantId, It.IsAny<Guid>(), It.IsAny<CancellationToken>())).ReturnsAsync(Reviewer);
        _scope.Setup(s => s.ResolveAsync(false, It.IsAny<IReadOnlyCollection<Guid>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ExceptionScope(false, Reviewer, [Subject]));
        _scope.Setup(s => s.ResolveAsync(true, It.IsAny<IReadOnlyCollection<Guid>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((ExceptionScope?)null);
        var resolver = new ExceptionConversationSubjectResolver(_exceptions.Object, _scope.Object,
            new InboxEscalationPolicyRegistry([_policy.Object]), currentUser.Object, identity.Object);
        var item = new ItemRef("monitoring", "exception_case", c.Id);

        (await resolver.CanViewAsync(TenantId, item, Reviewer, default)).Should().BeTrue();
        (await resolver.CanReviewAsync(TenantId, item, Reviewer, default)).Should().BeFalse();
    }

    // ---- D-b: camera verification skipped ----------------------------------------------------

    [Fact]
    public async Task Clocking_in_without_the_required_camera_check_opens_one_identity_case_per_day()
    {
        var workDate = new DateOnly(2026, 10, 13);
        var skipped = new Employee { Id = Guid.NewGuid(), UserId = Guid.NewGuid() };
        var scanned = new Employee { Id = Guid.NewGuid(), UserId = Guid.NewGuid() };
        var alreadyOpen = new Employee { Id = Guid.NewGuid(), UserId = Guid.NewGuid() };
        var start = new DateTimeOffset(2026, 10, 13, 9, 0, 0, TimeSpan.Zero);
        var attendance = new Mock<IAttendanceReadRepository>();
        attendance.Setup(a => a.ListRecordsForDateAsync(TenantId, It.IsAny<IReadOnlyCollection<Guid>>(), workDate, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new[] { skipped, scanned, alreadyOpen }.Select(e => new AttendanceRecord { EmployeeId = e.Id, ActualStart = start }).ToList());
        var employees = new Mock<IEmployeeRepository>();
        employees.Setup(e => e.ListByIdsAsync(TenantId, It.IsAny<IReadOnlyCollection<Guid>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new[] { skipped, scanned, alreadyOpen }.ToDictionary(e => e.Id));
        var checkIns = new Mock<ICheckInRepository>();
        checkIns.Setup(c => c.ListForUsersInRangeAsync(TenantId, It.IsAny<IReadOnlyCollection<Guid>>(), It.IsAny<DateTimeOffset>(), It.IsAny<DateTimeOffset>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([new EmployeeCheckIn { UserId = scanned.UserId, CheckedInAt = start, FaceScanId = Guid.NewGuid() }]);
        var toggles = new Mock<IMonitoringToggleResolver>();
        toggles.Setup(t => t.IsEnabledAsync(TenantId, It.IsAny<Guid>(), MonitoringCapability.IdentityVerification, It.IsAny<CancellationToken>())).ReturnsAsync(true);
        _exceptions.Setup(x => x.HasUnresolvedSinceAsync(TenantId, It.IsAny<Guid>(), ExceptionType.IdentityAnomaly, It.IsAny<DateTimeOffset>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Guid _, Guid employeeId, ExceptionType _, DateTimeOffset _, CancellationToken _) => employeeId == alreadyOpen.Id);

        var cases = await new CameraVerificationSkippedDetector(attendance.Object, checkIns.Object, employees.Object, toggles.Object, _exceptions.Object)
            .DetectAsync(TenantId, [skipped.Id, scanned.Id, alreadyOpen.Id], workDate, Now, default);

        var created = cases.Should().ContainSingle().Subject;
        (created.EmployeeId, created.Type, created.Status).Should().Be((skipped.Id, ExceptionType.IdentityAnomaly, ExceptionStatus.Open));
        ExceptionMetadata.Parse(created.MetadataJson).Source.Should().Be(ExceptionMetadata.SourceCameraVerificationSkipped);
    }

    private static readonly DateTimeOffset MonthStart = new(2026, 10, 1, 0, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset MonthEnd = new(2026, 11, 1, 0, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task ListPersonCases_QueriesTheSubjectAndRange_IncludingResolvedCases()
    {
        ExceptionListFilter? used = null;
        _exceptions.Setup(x => x.GetListAsync(TenantId, It.IsAny<ExceptionListFilter>(), 1, It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .Callback((Guid _, ExceptionListFilter f, int _, int _, CancellationToken _) => used = f)
            .ReturnsAsync(() => _cases.ToList());
        Case(ExceptionStatus.Resolved);
        Case(ExceptionStatus.Open);
        ScopeIs(isHr: false, Subject);

        var rows = await Provider().ListPersonCasesAsync(TenantId, Subject, InboxCategories.Alert, MonthStart, MonthEnd, Reviewer, default);

        rows.Should().HaveCount(2);
        rows.Select(r => r.Item.Status).Should().BeEquivalentTo("resolved", "open");
        used!.EmployeeIds.Should().Equal(Subject);
        used.ActiveOnly.Should().BeFalse();
        used.Calendar.Should().BeFalse();
        (used.DetectedFrom, used.DetectedTo).Should().Be((MonthStart, MonthEnd));
    }

    [Fact]
    public async Task ListPersonCases_ExcludesCalendarTypes()
    {
        Case().Type = ExceptionType.MeetingNoShow;
        ScopeIs(isHr: false, Subject);

        (await Provider().ListPersonCasesAsync(TenantId, Subject, InboxCategories.Alert, MonthStart, MonthEnd, Reviewer, default))
            .Should().BeEmpty();
    }

    [Fact]
    public async Task ListPersonCases_RequestCategoryReturnsEmpty()
    {
        Case();

        (await Provider().ListPersonCasesAsync(TenantId, Subject, InboxCategories.Request, MonthStart, MonthEnd, Reviewer, default))
            .Should().BeEmpty();
        _exceptions.Verify(x => x.GetListAsync(It.IsAny<Guid>(), It.IsAny<ExceptionListFilter>(), It.IsAny<int>(), It.IsAny<int>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task ListPersonCases_CanOpenTrueInScope_FalseOutOfScope()
    {
        Case();
        ScopeIs(isHr: false, Subject);
        var inScope = await Provider().ListPersonCasesAsync(TenantId, Subject, InboxCategories.Alert, MonthStart, MonthEnd, Reviewer, default);
        ScopeIs(isHr: false);
        var outOfScope = await Provider().ListPersonCasesAsync(TenantId, Subject, InboxCategories.Alert, MonthStart, MonthEnd, Reviewer, default);

        inScope.Single().CanOpen.Should().BeTrue();
        outOfScope.Single().CanOpen.Should().BeFalse();
    }

    [Fact]
    public async Task ListPersonCases_MapsAsInfoWithNoActions()
    {
        Case();
        ScopeIs(isHr: false, Subject);
        ApproverOf(Subject);

        var row = (await Provider().ListPersonCasesAsync(TenantId, Subject, InboxCategories.Alert, MonthStart, MonthEnd, Reviewer, default)).Single();

        row.Item.Direction.Should().Be(InboxDirections.Info);
        row.Item.AvailableActions.Should().BeEmpty();
        row.Item.RequestedBy!.Id.Should().Be(Subject);
    }
}
