using System.Text.Json;
using FluentAssertions;
using MediatR;
using Microsoft.Extensions.Options;
using Moq;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Common.RepositoryInterfaces;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.Conversations.Contracts;
using ONEVO.Application.Features.Conversations.RepositoryInterfaces;
using ONEVO.Application.Features.Conversations.Services;
using ONEVO.Application.Features.Inbox.Contracts;
using ONEVO.Application.Features.Leave.Approval.Commands;
using ONEVO.Application.Features.Leave.Approval.DTOs.Responses;
using ONEVO.Application.Features.Leave.Approval.Options;
using ONEVO.Application.Features.Leave.Approval.RepositoryInterfaces;
using ONEVO.Application.Features.Leave.Approval.Services;
using ONEVO.Application.Features.Leave.Cancellation.Helpers;
using ONEVO.Application.Features.Leave.Inbox;
using ONEVO.Application.Features.Leave.Request.RepositoryInterfaces;
using ONEVO.Application.Features.Leave.Request.Services;
using ONEVO.Domain.Features.Conversations.Entities;
using ONEVO.Domain.Features.CoreHr.Entities;
using ONEVO.Domain.Features.Inbox.Entities;
using ONEVO.Domain.Features.Leave.BalanceAudit.Entities;
using ONEVO.Domain.Features.Leave.Common;
using ONEVO.Domain.Features.Leave.Entitlement.Entities;
using ONEVO.Domain.Features.Leave.Request.Entities;
using ONEVO.Tests.Unit.Fakes;
using ONEVO.Tests.Unit.Features.Inbox;
using ONEVO.Tests.Unit.Features.Leave.Request;
using Xunit;

namespace ONEVO.Tests.Unit.Features.Leave.Inbox;

/// <summary>Leave in the Inbox (plan L1): edited approval, the provider, and the 30-minute revert.</summary>
public class LeaveInboxTests
{
    private static DateTimeOffset At(int day, int hour) => new(2026, 8, day, hour, 0, 0, TimeSpan.Zero);

    private static LeaveForwardTargetResolver NoForwardTarget()
    {
        var approvers = new Mock<ILeaveApproverResolver>();
        approvers.Setup(a => a.ResolveAsync(It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<DateOnly>(), It.IsAny<DateOnly>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new LeaveApproverResolution([]));
        return new LeaveForwardTargetResolver(approvers.Object);
    }

    private static LeaveApprovalState State(
        Employee employee, LeaveEntitlement? entitlement, Guid leaveTypeId, string mode, params LeaveRequestApprover[] approvers)
    {
        var request = new LeaveRequest
        {
            Id = approvers.FirstOrDefault()?.LeaveRequestId ?? Guid.NewGuid(), TenantId = employee.TenantId, EmployeeId = employee.Id,
            LeaveTypeId = leaveTypeId, StartAt = At(18, 9), EndAt = At(18, 18), TotalHours = 8m, PaidHours = 8m,
            Status = LeaveRequestStatuses.Pending, CreatedAt = At(10, 9),
        };
        return new LeaveApprovalState(request, entitlement, employee, "Annual Leave", "AL", mode, approvers.ToList(), []);
    }

    private static LeaveRequestApprover Approver(Guid requestId, Guid employeeId, int sequence, string status = LeaveRequestApproverStatuses.Pending)
        => new() { Id = Guid.NewGuid(), LeaveRequestId = requestId, ApproverEmployeeId = employeeId, SequenceOrder = sequence, Status = status };

    // ---- edit before approve (range editor + command) -----------------------------------------

    private sealed class EditFixture
    {
        public LeaveRequestSubmissionEvaluatorTests.Harness Harness { get; }
        public Mock<ILeaveApprovalRepository> Repository { get; } = new();
        public Mock<ICurrentUser> CurrentUser { get; } = new();
        public List<LeaveRequestDayAllocation> Allocations { get; } = new();
        public List<LeaveRequestDayAllocation> AddedAllocations { get; } = new();
        public List<LeaveBalanceAudit> Audits { get; } = new();
        public bool CanApprove { get; set; } = true;
        public LeaveApprovalState State { get; }

        public EditFixture(decimal remaining)
        {
            Harness = LeaveRequestSubmissionEvaluatorTests.Harness.Create(remainingHours: remaining);
            Harness.Requests.Setup(x => x.HasOverlappingPendingOrApprovedRequestExceptAsync(
                    Harness.TenantId, Harness.Employee.Id, It.IsAny<DateTimeOffset>(), It.IsAny<DateTimeOffset>(), It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(false);
            Harness.Entitlement.PendingHours = 8m;
            State = LeaveInboxTests.State(Harness.Employee, Harness.Entitlement, Harness.LeaveTypeId, LeaveApprovalModes.AnyOne);
            Allocations.Add(new LeaveRequestDayAllocation
            {
                Id = Guid.NewGuid(), LeaveRequestId = State.Request.Id, LeaveDate = new DateOnly(2026, 8, 18), HoursUnit = 8m, PaidHoursUnit = 8m
            });
            CurrentUser.SetupGet(x => x.TenantId).Returns(Harness.TenantId);
            CurrentUser.SetupGet(x => x.UserId).Returns(Harness.UserId);
            CurrentUser.SetupGet(x => x.IsAuthenticated).Returns(true);
            CurrentUser.Setup(x => x.HasPermission("leave:approve")).Returns(true);
            Repository.Setup(x => x.ListActiveAllocationsAsync(Harness.TenantId, State.Request.Id, It.IsAny<CancellationToken>())).ReturnsAsync(Allocations);
            Repository.Setup(x => x.AddAllocationsAsync(It.IsAny<IReadOnlyCollection<LeaveRequestDayAllocation>>(), It.IsAny<CancellationToken>()))
                .Callback((IReadOnlyCollection<LeaveRequestDayAllocation> a, CancellationToken _) => AddedAllocations.AddRange(a)).Returns(Task.CompletedTask);
            Repository.Setup(x => x.AddBalanceAuditAsync(It.IsAny<LeaveBalanceAudit>(), It.IsAny<CancellationToken>()))
                .Callback((LeaveBalanceAudit a, CancellationToken _) => Audits.Add(a)).Returns(Task.CompletedTask);
        }

        public LeaveRequestRangeEditor Editor()
            => new(CurrentUser.Object, Harness.Clock.Object, Repository.Object, Harness.Sut, new LeaveRequestDayAllocationBuilder());
    }

    [Fact]
    public async Task An_edited_range_reruns_the_leave_rules_and_rewrites_hours_allocations_reserved_hours_and_the_audit()
    {
        var f = new EditFixture(remaining: 160m);

        var result = await f.Editor().ApplyAsync(f.State, At(18, 9), At(19, 18), "Dates changed by the approver", default, allowLonger: true);

        result.IsSuccess.Should().BeTrue(result.Error);
        f.State.Request.EndAt.Should().Be(At(19, 18));
        f.State.Request.PaidHours.Should().BeGreaterThan(8m);
        f.Harness.Entitlement.PendingHours.Should().Be(f.State.Request.PaidHours);
        f.Allocations.Should().OnlyContain(a => a.Status == LeaveRequestDayAllocationStatuses.Cancelled);
        f.AddedAllocations.Sum(a => a.HoursUnit).Should().Be(f.State.Request.TotalHours);
        f.Audits.Should().ContainSingle(a => a.ChangeType == LeaveBalanceChangeTypes.Adjustment && a.RelatedRequestId == f.State.Request.Id);
        // The request itself is left out of the overlap check.
        f.Harness.Requests.Verify(x => x.HasOverlappingPendingOrApprovedRequestExceptAsync(f.Harness.TenantId, f.Harness.Employee.Id,
            At(18, 9), At(19, 18), f.State.Request.Id, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task An_edited_range_that_breaks_a_leave_rule_is_refused_with_its_message_and_nothing_changes()
    {
        var f = new EditFixture(remaining: 8m); // only this request's own 8 h are available

        var result = await f.Editor().ApplyAsync(f.State, At(18, 9), At(19, 18), "x", default, allowLonger: true);

        result.StatusCode.Should().Be(422);
        result.Error.Should().Contain("balance", "the evaluator's own insufficient-balance message is returned");
        (f.State.Request.StartAt, f.State.Request.EndAt, f.State.Request.PaidHours).Should().Be((At(18, 9), At(18, 18), 8m));
        f.Harness.Entitlement.PendingHours.Should().Be(8m);
        f.AddedAllocations.Should().BeEmpty();
        f.Audits.Should().BeEmpty();
    }

    [Fact]
    public async Task An_approver_can_shorten_or_move_a_request_but_never_make_it_longer()
    {
        var f = new EditFixture(remaining: 160m);

        var longer = await f.Editor().ApplyAsync(f.State, At(18, 9), At(19, 18), "x", default, allowLonger: false);
        var moved = await f.Editor().ApplyAsync(f.State, At(19, 9), At(19, 18), "x", default, allowLonger: false);

        longer.StatusCode.Should().Be(422);
        longer.Error.Should().Be(LeaveRequestRangeEditor.CannotLengthen);
        moved.IsSuccess.Should().BeTrue(moved.Error);
        f.State.Request.StartAt.Should().Be(At(19, 9));
    }

    [Fact]
    public async Task Edited_approve_with_a_rule_error_returns_422_and_never_approves()
    {
        var f = new EditFixture(remaining: 8m);
        var approver = new Employee { Id = Guid.NewGuid(), TenantId = f.Harness.TenantId, UserId = f.Harness.UserId };
        var state = LeaveInboxTests.State(f.Harness.Employee, f.Harness.Entitlement, f.Harness.LeaveTypeId, LeaveApprovalModes.AnyOne,
            Approver(f.State.Request.Id, approver.Id, 1));
        f.Repository.Setup(x => x.GetStateAsync(f.Harness.TenantId, state.Request.Id, It.IsAny<CancellationToken>())).ReturnsAsync(state);
        var employees = new Mock<IEmployeeRepository>();
        employees.Setup(x => x.GetByUserIdAsync(f.Harness.TenantId, f.Harness.UserId, It.IsAny<CancellationToken>())).ReturnsAsync(approver);
        var decisions = new LeaveApprovalDecisionService(f.CurrentUser.Object, f.Harness.Clock.Object, employees.Object, f.Repository.Object,
            Mock.Of<IOutboxWriter>(), Mock.Of<INotificationDispatcher>(), Mock.Of<ILeaveRequestConflictProvider>(),
            NoForwardTarget(),
            Options.Create(new LeaveApprovalOptions()));
        var uow = new FakeUnitOfWork();
        var handler = new ApproveEditedLeaveRequestCommandHandler(f.CurrentUser.Object, employees.Object, f.Repository.Object,
            new LeaveRequestRangeEditor(f.CurrentUser.Object, f.Harness.Clock.Object, f.Repository.Object, f.Harness.Sut, new LeaveRequestDayAllocationBuilder()),
            decisions, uow);

        var result = await handler.Handle(new ApproveEditedLeaveRequestCommand(state.Request.Id, At(18, 9), At(19, 18), null), default);

        result.StatusCode.Should().Be(422);
        state.Request.Status.Should().Be(LeaveRequestStatuses.Pending);
        state.Approvers.Single().Status.Should().Be(LeaveRequestApproverStatuses.Pending);
        f.Repository.Verify(x => x.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    // ---- provider ---------------------------------------------------------------------------

    private sealed class ProviderFixture
    {
        public Guid TenantId { get; } = Guid.NewGuid();
        public Guid LeaveTypeId { get; } = Guid.NewGuid();
        public Employee Requester { get; }
        public Employee First { get; }
        public Employee Second { get; }
        public Mock<ICurrentUser> CurrentUser { get; } = new();
        public Mock<IEmployeeRepository> Employees { get; } = new();
        public Mock<ILeaveApprovalRepository> Approvals { get; } = new();
        public Mock<ILeaveRequestRepository> Requests { get; } = new();
        public Mock<ISender> Sender { get; } = new();
        public InMemoryInboxDecisionJournal Journal { get; } = new();
        public FakeUnitOfWork UnitOfWork { get; } = new();
        public Mock<IConversationPoster> Poster { get; } = new();
        public Mock<IConversationRepository> Conversations { get; } = new();
        public List<LeaveApprovalState> States { get; } = new();

        public ProviderFixture()
        {
            Requester = Person("Bala");
            First = Person("Anu");
            Second = Person("Chen");
            foreach (var p in new[] { Requester, First, Second })
                Employees.Setup(x => x.GetByIdAsync(TenantId, p.Id, It.IsAny<CancellationToken>())).ReturnsAsync(p);
            Employees.Setup(x => x.ListByIdsAsync(TenantId, It.IsAny<IReadOnlyCollection<Guid>>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(new Dictionary<Guid, Employee> { [Requester.Id] = Requester, [First.Id] = First, [Second.Id] = Second });
            CurrentUser.Setup(x => x.HasPermission("leave:approve")).Returns(true);
            CurrentUser.Setup(x => x.HasPermission("leave:read-own")).Returns(true);
            Approvals.Setup(x => x.ListPendingForApproverAsync(TenantId, It.IsAny<Guid>(), It.IsAny<LeaveApprovalListFilter>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(() => States.Select(s => new LeavePendingApprovalListRow(s.Request, "Bala", "Annual Leave", "AL")).ToList());
            Approvals.Setup(x => x.GetStateAsync(TenantId, It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync((Guid _, Guid id, CancellationToken _) => States.FirstOrDefault(s => s.Request.Id == id));
            Approvals.Setup(x => x.ListApprovalHistoryAsync(TenantId, It.IsAny<Guid>(), It.IsAny<DateOnly?>(), It.IsAny<DateOnly?>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync([]);
            Approvals.Setup(x => x.ListSubmittedOnBehalfAsync(TenantId, It.IsAny<Guid>(), It.IsAny<CancellationToken>())).ReturnsAsync([]);
            Requests.Setup(x => x.ListOwnAsync(TenantId, It.IsAny<Guid>(), It.IsAny<LeaveRequestListFilter>(), It.IsAny<CancellationToken>())).ReturnsAsync([]);
            Conversations.Setup(x => x.ListAddedSubjectsAsync(TenantId, It.IsAny<Guid>(), "leave", It.IsAny<CancellationToken>())).ReturnsAsync([]);
        }

        private Employee Person(string name) => new() { Id = Guid.NewGuid(), TenantId = TenantId, UserId = Guid.NewGuid(), FirstName = name, LegalEntityId = Guid.NewGuid() };

        public LeaveApprovalState Add(string mode, params (Employee Who, string Status)[] approvers)
        {
            var requestId = Guid.NewGuid();
            var rows = approvers.Select((a, i) => Approver(requestId, a.Who.Id, i + 1, a.Status)).ToArray();
            var state = State(Requester, new LeaveEntitlement { TotalHours = 160m, PendingHours = 8m }, LeaveTypeId, mode, rows);
            States.Add(state);
            return state;
        }

        public LeaveInboxSourceProvider Provider() => new(
            CurrentUser.Object, Employees.Object, Approvals.Object, Requests.Object,
            NoForwardTarget(),
            Sender.Object, Journal, UnitOfWork, Poster.Object, Conversations.Object, new FakeDateTimeProvider { UtcNow = At(15, 9) });
    }

    [Fact]
    public async Task Received_lists_only_requests_the_caller_can_act_on_now_respecting_in_order()
    {
        var f = new ProviderFixture();
        var state = f.Add(LeaveApprovalModes.InOrder, (f.First, LeaveRequestApproverStatuses.Pending), (f.Second, LeaveRequestApproverStatuses.Pending));

        var forFirst = await f.Provider().ListAsync(f.TenantId, new InboxQuery(), f.First.Id, default);
        var forSecond = await f.Provider().ListAsync(f.TenantId, new InboxQuery(), f.Second.Id, default);

        forFirst.Should().ContainSingle(i => i.Id == state.Request.Id && i.Direction == InboxDirections.Received && i.IsPending);
        forSecond.Should().BeEmpty();
    }

    [Fact]
    public async Task After_a_forward_the_new_approver_sees_the_request_and_the_old_one_does_not()
    {
        var f = new ProviderFixture();
        var state = f.Add(LeaveApprovalModes.AnyOne, (f.First, LeaveRequestApproverStatuses.Forwarded), (f.Second, LeaveRequestApproverStatuses.Pending));

        (await f.Provider().ListAsync(f.TenantId, new InboxQuery(), f.Second.Id, default))
            .Should().ContainSingle(i => i.Id == state.Request.Id && i.IsPending);
        (await f.Provider().ListAsync(f.TenantId, new InboxQuery(), f.First.Id, default)).Should().BeEmpty();
    }

    [Fact]
    public async Task Sent_includes_requests_submitted_on_someones_behalf()
    {
        var f = new ProviderFixture();
        var request = new LeaveRequest
        {
            Id = Guid.NewGuid(), TenantId = f.TenantId, EmployeeId = f.Requester.Id, LeaveTypeId = f.LeaveTypeId, Status = LeaveRequestStatuses.Pending,
            StartAt = At(20, 9), EndAt = At(20, 18), TotalHours = 8m, SubmittedOnBehalfOfBy = f.First.UserId
        };
        f.Approvals.Setup(x => x.ListSubmittedOnBehalfAsync(f.TenantId, f.First.UserId, It.IsAny<CancellationToken>())).ReturnsAsync([request]);
        f.Approvals.Setup(x => x.ListLeaveTypeNamesAsync(f.TenantId, It.IsAny<IReadOnlyCollection<Guid>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Dictionary<Guid, string> { [f.LeaveTypeId] = "Sick Leave" });

        var items = await f.Provider().ListAsync(f.TenantId, new InboxQuery(), f.First.Id, default);

        items.Should().ContainSingle(i => i.Id == request.Id && i.Direction == InboxDirections.Sent && i.Title == "Sick Leave - Bala");
    }

    [Fact]
    public async Task A_member_added_to_the_chat_sees_the_request_but_gets_no_decision_actions()
    {
        var f = new ProviderFixture();
        var state = f.Add(LeaveApprovalModes.AnyOne, (f.First, LeaveRequestApproverStatuses.Pending));
        f.Conversations.Setup(x => x.ListAddedSubjectsAsync(f.TenantId, f.Second.Id, "leave", It.IsAny<CancellationToken>()))
            .ReturnsAsync([new ItemRef("leave", "request", state.Request.Id)]);

        var asAdded = await f.Provider().GetDetailAsync(f.TenantId, "request", state.Request.Id, f.Second.Id, default);
        var asApprover = await f.Provider().GetDetailAsync(f.TenantId, "request", state.Request.Id, f.First.Id, default);

        asAdded.Should().NotBeNull();
        asAdded!.Item.AvailableActions.Should().BeEmpty();
        asAdded.ChangeSet.Should().OnlyContain(c => !c.Editable);
        asApprover!.Item.AvailableActions.Select(a => a.Key).Should().Contain(["approve", "reject", "request_info"]);
        asApprover.ChangeSet.Where(c => c.Editable).Select(c => c.Field).Should().Equal("startAt", "endAt");
    }

    [Fact]
    public async Task Ask_for_information_runs_the_leave_command_posts_the_question_into_the_chat_and_journals_it()
    {
        var f = new ProviderFixture();
        var state = f.Add(LeaveApprovalModes.AnyOne, (f.First, LeaveRequestApproverStatuses.Pending));
        f.Sender.Setup(s => s.Send(It.Is<RequestLeaveInformationCommand>(c => c.RequestId == state.Request.Id && c.Question == "Which dates exactly?"),
                It.IsAny<CancellationToken>()))
            .Callback(() => state.Request.Status = LeaveRequestStatuses.InformationRequested)
            .ReturnsAsync(Result<LeaveApprovalDecisionResponse>.Success(null!));
        var published = false;
        f.Poster.Setup(p => p.PostAsync(f.TenantId, new ItemRef("leave", "request", state.Request.Id), f.First.Id, "Which dates exactly?",
                ConversationMessageTags.InfoRequest, It.IsAny<CancellationToken>()))
            .ReturnsAsync(() => () => { published = true; return Task.CompletedTask; });

        var result = await f.Provider().ExecuteActionAsync(f.TenantId, "request", state.Request.Id, "request_info",
            JsonSerializer.SerializeToElement(new { comment = "Which dates exactly?" }), f.First.Id, default);

        result.IsSuccess.Should().BeTrue(result.Error);
        published.Should().BeTrue("the chat message is announced after the commit");
        var row = f.Journal.Entries.Should().ContainSingle().Subject;
        (row.Action, row.ActorEmployeeId).Should().Be(("request_info", f.First.Id));
        LeaveDecisionSnapshot.Parse(row.BeforeJson)!.Status.Should().Be(LeaveRequestStatuses.Pending);
        LeaveDecisionSnapshot.Parse(row.AfterJson)!.Status.Should().Be(LeaveRequestStatuses.InformationRequested);
        result.Value!.AffectedEmployeeIds.Should().BeEquivalentTo([f.Requester.Id, f.First.Id]);
    }

    [Fact]
    public async Task Approve_with_changed_dates_goes_through_edited_approval_and_unchanged_dates_through_plain_approval()
    {
        var f = new ProviderFixture();
        var state = f.Add(LeaveApprovalModes.AnyOne, (f.First, LeaveRequestApproverStatuses.Pending));
        f.Sender.Setup(s => s.Send(It.IsAny<IRequest<Result<LeaveApprovalDecisionResponse>>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<LeaveApprovalDecisionResponse>.Success(null!));
        JsonElement Body(DateTimeOffset start, DateTimeOffset end) => JsonSerializer.SerializeToElement(new
        {
            editedPayloadJson = JsonSerializer.Serialize(new { startAt = start, endAt = end })
        });

        await f.Provider().ExecuteActionAsync(f.TenantId, "request", state.Request.Id, "approve", Body(At(18, 9), At(18, 18)), f.First.Id, default);
        await f.Provider().ExecuteActionAsync(f.TenantId, "request", state.Request.Id, "approve", Body(At(18, 13), At(18, 18)), f.First.Id, default);

        f.Sender.Verify(s => s.Send(It.Is<ApproveLeaveRequestCommand>(c => c.RequestId == state.Request.Id), It.IsAny<CancellationToken>()), Times.Once);
        f.Sender.Verify(s => s.Send(It.Is<ApproveEditedLeaveRequestCommand>(c => c.StartAt == At(18, 13)), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Without_leave_approve_an_approver_row_neither_lists_nor_decides_through_the_inbox()
    {
        var f = new ProviderFixture();
        var state = f.Add(LeaveApprovalModes.AnyOne, (f.First, LeaveRequestApproverStatuses.Pending));
        f.CurrentUser.Setup(x => x.HasPermission("leave:approve")).Returns(false);

        var listed = await f.Provider().ListAsync(f.TenantId, new InboxQuery(), f.First.Id, default);
        var approve = await f.Provider().ExecuteActionAsync(f.TenantId, "request", state.Request.Id, "approve", null, f.First.Id, default);
        var bulk = await f.Provider().ExecuteBulkAsync(f.TenantId, "request", "approve", [state.Request.Id],
            JsonSerializer.SerializeToElement(new { }), f.First.Id, default);

        listed.Should().BeEmpty();
        approve.StatusCode.Should().Be(403);
        bulk!.Should().OnlyContain(r => r.Result.StatusCode == 403);
        f.Sender.Invocations.Should().BeEmpty();
        f.Journal.Entries.Should().BeEmpty();
    }

    [Fact]
    public async Task Replying_to_an_information_request_needs_the_employee_self_service_permission()
    {
        var f = new ProviderFixture();
        var state = f.Add(LeaveApprovalModes.AnyOne, (f.First, LeaveRequestApproverStatuses.InformationRequested));
        f.CurrentUser.Setup(x => x.HasPermission("leave:read-own")).Returns(false);

        var result = await f.Provider().ExecuteActionAsync(f.TenantId, "request", state.Request.Id, "respond_info",
            JsonSerializer.SerializeToElement(new { comment = "here" }), f.Requester.Id, default);

        result.StatusCode.Should().Be(403);
        f.Sender.Invocations.Should().BeEmpty();
    }

    [Fact]
    public async Task A_refused_module_action_writes_no_journal_row()
    {
        var f = new ProviderFixture();
        var state = f.Add(LeaveApprovalModes.AnyOne, (f.First, LeaveRequestApproverStatuses.Pending));
        f.Sender.Setup(s => s.Send(It.IsAny<RejectLeaveRequestCommand>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<LeaveApprovalDecisionResponse>.Forbidden("You are not an approver for this request."));

        var result = await f.Provider().ExecuteActionAsync(f.TenantId, "request", state.Request.Id, "reject",
            JsonSerializer.SerializeToElement(new { comment = "no" }), f.Second.Id, default);

        result.StatusCode.Should().Be(403);
        f.Journal.Entries.Should().BeEmpty();
    }

    // ---- revert -----------------------------------------------------------------------------

    private sealed class RevertFixture
    {
        public Guid TenantId { get; } = Guid.NewGuid();
        public Employee Requester { get; }
        public Employee Approver { get; }
        public LeaveApprovalState State { get; }
        public Mock<ILeaveApprovalRepository> Approvals { get; } = new();
        public InMemoryInboxDecisionJournal Journal { get; } = new();
        public FakeDateTimeProvider Clock { get; } = new() { UtcNow = At(15, 9) };
        public List<LeaveBalanceAudit> Audits { get; } = new();
        public bool CanApprove { get; set; } = true;

        public RevertFixture(decimal totalHours = 160m)
        {
            Requester = new Employee { Id = Guid.NewGuid(), TenantId = TenantId, FirstName = "Bala" };
            Approver = new Employee { Id = Guid.NewGuid(), TenantId = TenantId, FirstName = "Anu" };
            var requestId = Guid.NewGuid();
            State = LeaveInboxTests.State(Requester, new LeaveEntitlement { TotalHours = totalHours, PendingHours = 8m }, Guid.NewGuid(),
                LeaveApprovalModes.AnyOne, LeaveInboxTests.Approver(requestId, Approver.Id, 1));
            Approvals.Setup(x => x.GetStateAsync(TenantId, State.Request.Id, It.IsAny<CancellationToken>())).ReturnsAsync(State);
            Approvals.Setup(x => x.ListBalanceAuditsSinceAsync(TenantId, Requester.Id, State.Request.LeaveTypeId, It.IsAny<DateTimeOffset>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync([]);
            Approvals.Setup(x => x.AddBalanceAuditAsync(It.IsAny<LeaveBalanceAudit>(), It.IsAny<CancellationToken>()))
                .Callback((LeaveBalanceAudit a, CancellationToken _) => Audits.Add(a)).Returns(Task.CompletedTask);
        }

        /// <summary>Applies a decision to the state the way the module would, and journals it with before/after.</summary>
        public void Decide(string action, Action<LeaveApprovalState> apply)
        {
            var before = LeaveDecisionSnapshot.Of(State).ToJson();
            apply(State);
            Journal.Add(TenantId, new ItemRef("leave", "request", State.Request.Id), action, Approver.Id, Clock.UtcNow.AddMinutes(-5),
                LeaveDecisionSnapshot.Of(State).ToJson()).BeforeJson = before;
        }

        public LeaveDecisionReverter Reverter()
        {
            var currentUser = new Mock<ICurrentUser>();
            currentUser.Setup(x => x.HasPermission("leave:approve")).Returns(CanApprove);
            return new LeaveDecisionReverter(Journal, new FakeUnitOfWork(), Clock, Approvals.Object,
                new LeaveRequestRangeEditor(currentUser.Object, Clock, Approvals.Object, null!, new LeaveRequestDayAllocationBuilder()), currentUser.Object);
        }

        public Task<Result<InboxActionResult>> Revert()
            => Reverter().RevertAsync(TenantId, new ItemRef("leave", "request", State.Request.Id), Approver.Id, null, default);
    }

    private static void Approve(LeaveApprovalState s)
    {
        s.Approvers[0].Status = LeaveRequestApproverStatuses.Approved;
        s.Request.Status = LeaveRequestStatuses.Approved;
        s.Entitlement!.PendingHours -= 8m;
        s.Entitlement.UsedHours += 8m;
    }

    [Fact]
    public async Task Undoing_an_approval_puts_the_request_back_to_pending_and_the_hours_back_to_reserved()
    {
        var f = new RevertFixture();
        f.Decide(LeaveInboxActions.Approve, Approve);

        var result = await f.Revert();

        result.IsSuccess.Should().BeTrue(result.Error);
        f.State.Request.Status.Should().Be(LeaveRequestStatuses.Pending);
        f.State.Approvers[0].Status.Should().Be(LeaveRequestApproverStatuses.Pending);
        (f.State.Entitlement!.UsedHours, f.State.Entitlement.PendingHours).Should().Be((0m, 8m));
        f.Audits.Should().ContainSingle(a => a.ChangeType == LeaveBalanceChangeTypes.Adjustment);
        f.Approvals.Verify(x => x.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Someone_who_lost_leave_approve_cannot_undo_their_decision()
    {
        var f = new RevertFixture();
        f.Decide(LeaveInboxActions.Approve, Approve);
        f.CanApprove = false;

        var result = await f.Revert();

        result.StatusCode.Should().Be(403);
        f.State.Request.Status.Should().Be(LeaveRequestStatuses.Approved);
    }

    [Fact]
    public async Task An_approval_cannot_be_undone_once_the_leave_has_started()
    {
        var f = new RevertFixture();
        f.Decide(LeaveInboxActions.Approve, Approve);
        f.State.Request.StartAt = f.Clock.UtcNow.AddMinutes(-1);

        var result = await f.Revert();

        result.StatusCode.Should().Be(409);
        result.Error.Should().Be(LeaveDecisionReverter.AlreadyStarted);
        f.State.Request.Status.Should().Be(LeaveRequestStatuses.Approved);
    }

    [Fact]
    public async Task An_approval_cannot_be_undone_after_the_entitlement_was_recalculated()
    {
        var f = new RevertFixture();
        f.Decide(LeaveInboxActions.Approve, Approve);
        f.Approvals.Setup(x => x.ListBalanceAuditsSinceAsync(f.TenantId, f.Requester.Id, f.State.Request.LeaveTypeId, It.IsAny<DateTimeOffset>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([new LeaveBalanceAudit { ChangeType = LeaveBalanceChangeTypes.Accrual }]);

        (await f.Revert()).Error.Should().Be(LeaveDecisionReverter.BalanceChanged);
    }

    [Fact]
    public async Task A_rejection_cannot_be_undone_when_the_balance_no_longer_covers_the_request()
    {
        var f = new RevertFixture(totalHours: 8m);
        f.Decide(LeaveInboxActions.Reject, s =>
        {
            s.Approvers[0].Status = LeaveRequestApproverStatuses.Rejected;
            s.Request.Status = LeaveRequestStatuses.Rejected;
            s.Entitlement!.PendingHours -= 8m;
        });
        f.State.Entitlement!.UsedHours = 4m; // another leave was approved since

        var result = await f.Revert();

        result.StatusCode.Should().Be(409);
        result.Error.Should().Contain("can no longer cover");
    }

    [Fact]
    public async Task Undoing_a_forward_removes_the_added_approver_unless_they_already_acted()
    {
        var f = new RevertFixture();
        var target = new LeaveRequestApprover { Id = Guid.NewGuid(), LeaveRequestId = f.State.Request.Id, ApproverEmployeeId = Guid.NewGuid(),
            SequenceOrder = 1, Status = LeaveRequestApproverStatuses.Pending };
        f.Decide(LeaveInboxActions.Forward, s =>
        {
            s.Approvers[0].Status = LeaveRequestApproverStatuses.Forwarded;
            ((List<LeaveRequestApprover>)s.Approvers).Add(target);
        });

        target.Status = LeaveRequestApproverStatuses.Approved;
        (await f.Revert()).Error.Should().Be(LeaveDecisionReverter.ForwardActed);

        target.Status = LeaveRequestApproverStatuses.Pending;
        (await f.Revert()).IsSuccess.Should().BeTrue();
        f.Approvals.Verify(x => x.RemoveApprover(target), Times.Once);
        f.State.Approvers[0].Status.Should().Be(LeaveRequestApproverStatuses.Pending);
    }
}
