using FluentAssertions;
using Moq;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.Leave.Approval.Commands;
using ONEVO.Application.Features.Leave.Approval.OutboxHandlers;
using ONEVO.Application.Features.Leave.Approval.RepositoryInterfaces;
using ONEVO.Application.Features.Leave.Cancellation.Helpers;
using ONEVO.Application.Features.Leave.Policy.RepositoryInterfaces;
using ONEVO.Application.Features.Leave.Request.Helpers;
using ONEVO.Application.Features.Leave.Request.RepositoryInterfaces;
using ONEVO.Application.Features.Leave.Request.Services;
using ONEVO.Domain.Features.CoreHr.Entities;
using ONEVO.Domain.Features.Leave.BalanceAudit.Entities;
using ONEVO.Domain.Features.Leave.Common;
using ONEVO.Tests.Unit.Fakes;
using ONEVO.Tests.Unit.Features.Leave.Entitlement;
using ONEVO.Tests.Unit.Features.Leave.Request;

namespace ONEVO.Tests.Unit.Features.Leave.Approval;

public sealed class CreateApproverBackdatedLeaveCommandTests
{
    private static readonly DateOnly Friday = new(2026, 8, 14); // the harness's today is Monday 17 Aug

    private sealed class Fixture
    {
        public LeaveRequestSubmissionEvaluatorTests.Harness Harness { get; }
        public Employee Approver { get; }
        public Guid ApproverUserId { get; } = Guid.NewGuid();
        public Mock<ICurrentUser> CurrentUser { get; } = new();
        public Mock<ILeaveApprovalRepository> Approvals { get; } = new();
        public Mock<IOutboxWriter> Outbox { get; } = new();
        public Mock<INotificationDispatcher> Notifications { get; } = new();
        public List<LeaveRequestWriteSet> Written { get; } = new();
        public List<LeaveBalanceAudit> Audits { get; } = new();
        public List<object> Messages { get; } = new();
        public FakeUnitOfWork UnitOfWork { get; } = new();

        public Fixture(decimal remaining = 160m, bool callerIsInTheChain = true)
        {
            // Backdating is OFF for employees: only this command may bypass it.
            Harness = LeaveRequestSubmissionEvaluatorTests.Harness.Create(remainingHours: remaining, allowBackdated: false);
            Approver = new Employee
            {
                Id = Guid.NewGuid(), TenantId = Harness.TenantId, UserId = ApproverUserId, FirstName = "Mia", LastName = "Lead",
                EmployeeNumber = "E2", Email = "m@test.dev", LegalEntityId = Harness.LegalEntityId,
            };
            Harness.Employees.Setup(x => x.GetByUserIdAsync(Harness.TenantId, ApproverUserId, It.IsAny<CancellationToken>())).ReturnsAsync(Approver);
            var chain = callerIsInTheChain ? Approver.Id : Guid.NewGuid();
            Harness.Approvers.Setup(x => x.ResolveAsync(Harness.TenantId, Harness.Employee.Id, It.IsAny<DateOnly>(), It.IsAny<DateOnly>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(new LeaveApproverResolution([new LeaveApproverResolutionRow(chain, 1, null), new LeaveApproverResolutionRow(Guid.NewGuid(), 2, null)]));
            Harness.Requests.Setup(x => x.AddPendingRequestAsync(It.IsAny<LeaveRequestWriteSet>(), It.IsAny<CancellationToken>()))
                .Callback((LeaveRequestWriteSet w, CancellationToken _) => { w.Entitlement.PendingHours += w.Request.PaidHours; Written.Add(w); })
                .Returns(Task.CompletedTask);
            CurrentUser.SetupGet(x => x.TenantId).Returns(Harness.TenantId);
            CurrentUser.SetupGet(x => x.UserId).Returns(ApproverUserId);
            CurrentUser.SetupGet(x => x.IsAuthenticated).Returns(true);
            CurrentUser.Setup(x => x.HasPermission("leave:approve")).Returns(true);
            Approvals.Setup(x => x.AddBalanceAuditAsync(It.IsAny<LeaveBalanceAudit>(), It.IsAny<CancellationToken>()))
                .Callback((LeaveBalanceAudit a, CancellationToken _) => Audits.Add(a)).Returns(Task.CompletedTask);
            Outbox.Setup(x => x.EnqueueAsync(It.IsAny<string>(), It.IsAny<LeaveRequestApprovedPayload>(), It.IsAny<Guid?>(), It.IsAny<CancellationToken>()))
                .Callback((string type, LeaveRequestApprovedPayload payload, Guid? _, CancellationToken _) => Messages.Add(payload)).Returns(Task.CompletedTask);
        }

        public CreateApproverBackdatedLeaveCommandHandler Handler()
        {
            var policies = new Mock<ILeavePolicyRepository>();
            policies.Setup(x => x.ListActiveLegalEntitiesByIdsAsync(Harness.TenantId, It.IsAny<IReadOnlyCollection<Guid>>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync([LeaveTestData.CreateLegalEntity(Harness.TenantId, Harness.LegalEntityId)]);
            return new(CurrentUser.Object, Harness.Clock.Object, Harness.Employees.Object, policies.Object, Harness.Sut,
                Harness.Requests.Object, new LeaveRequestDayAllocationBuilder(), Approvals.Object, Outbox.Object, Notifications.Object, UnitOfWork);
        }

        public Task<ONEVO.Application.Common.Models.Result<Guid>> Run(DateOnly? day = null, Guid? employeeId = null)
            => Handler().Handle(new CreateApproverBackdatedLeaveCommand(employeeId ?? Harness.Employee.Id, Harness.LeaveTypeId, day ?? Friday), default);
    }

    [Fact]
    public async Task Creates_an_already_approved_leave_for_a_past_day_bypassing_only_the_backdating_rule()
    {
        var f = new Fixture();

        var result = await f.Run();

        result.IsSuccess.Should().BeTrue(result.Error);
        var written = f.Written.Should().ContainSingle().Subject;
        written.Request.Id.Should().Be(result.Value);
        written.Request.Status.Should().Be(LeaveRequestStatuses.Approved);
        written.Request.ApprovedBy.Should().Be(f.Approver.Id);
        written.Request.StartAt.Should().Be(new DateTimeOffset(2026, 8, 14, 9, 0, 0, TimeSpan.Zero));
        written.Request.EndAt.Should().Be(new DateTimeOffset(2026, 8, 14, 18, 0, 0, TimeSpan.Zero));
        written.Request.PaidHours.Should().BeGreaterThan(0m);
        written.Approvers.Select(a => (a.ApproverEmployeeId == f.Approver.Id, a.Status)).Should().BeEquivalentTo(new[]
        {
            (true, LeaveRequestApproverStatuses.Approved), (false, LeaveRequestApproverStatuses.Skipped),
        });
        // Hours are deducted, not left reserved.
        f.Harness.Entitlement.PendingHours.Should().Be(0m);
        f.Harness.Entitlement.UsedHours.Should().Be(written.Request.PaidHours);
        f.Audits.Should().ContainSingle(a => a.ChangeType == LeaveBalanceChangeTypes.Deduction && a.HoursChanged == -written.Request.PaidHours
            && a.RelatedRequestId == written.Request.Id);
        f.Messages.Should().ContainSingle().Which.Should().BeOfType<LeaveRequestApprovedPayload>()
            .Which.ApprovedByEmployeeId.Should().Be(f.Approver.Id);
        f.UnitOfWork.TransactionCallCount.Should().Be(1);
    }

    [Fact]
    public async Task Every_other_leave_rule_still_applies_and_nothing_is_written_when_one_fails()
    {
        var f = new Fixture(remaining: 2m);

        var result = await f.Run();

        result.StatusCode.Should().Be(422);
        result.Error.Should().Contain("balance");
        f.Written.Should().BeEmpty();
        f.Audits.Should().BeEmpty();
        f.Messages.Should().BeEmpty();
    }

    [Fact]
    public async Task Only_an_approver_of_this_employees_leave_may_do_it()
    {
        var notInChain = await new Fixture(callerIsInTheChain: false).Run();
        notInChain.StatusCode.Should().Be(403);

        var f = new Fixture();
        f.CurrentUser.Setup(x => x.HasPermission("leave:approve")).Returns(false);
        (await f.Run()).StatusCode.Should().Be(403);
        f.Written.Should().BeEmpty();
    }

    [Fact]
    public async Task Today_and_future_days_and_ones_own_leave_are_refused()
    {
        var f = new Fixture();

        (await f.Run(day: new DateOnly(2026, 8, 17))).StatusCode.Should().Be(422);
        (await f.Run(employeeId: f.Approver.Id)).StatusCode.Should().Be(409);
        f.Written.Should().BeEmpty();
    }
}
