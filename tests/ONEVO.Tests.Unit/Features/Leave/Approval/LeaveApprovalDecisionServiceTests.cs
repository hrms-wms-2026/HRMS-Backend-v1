using FluentAssertions;
using Microsoft.Extensions.Options;
using Moq;
using ONEVO.Application.Common.RepositoryInterfaces;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.Leave.Approval.Commands;
using ONEVO.Application.Features.Leave.Approval.Options;
using ONEVO.Application.Features.Leave.Approval.OutboxHandlers;
using ONEVO.Application.Features.Leave.Approval.RepositoryInterfaces;
using ONEVO.Application.Features.Leave.Approval.Services;
using ONEVO.Application.Features.Leave.Request.Services;
using ONEVO.Domain.Features.CoreHr.Entities;
using ONEVO.Domain.Features.Leave.BalanceAudit.Entities;
using ONEVO.Domain.Features.Leave.Common;
using ONEVO.Domain.Features.Leave.Entitlement.Entities;
using ONEVO.Domain.Features.Leave.Request.Entities;
using Xunit;

namespace ONEVO.Tests.Unit.Features.Leave.Approval;

public class LeaveApprovalDecisionServiceTests
{
    [Fact]
    public async Task ApproveAsync_WhenRequestAlreadyFinal_ReturnsConflict()
    {
        var harness = Harness.Create(LeaveRequestStatuses.Approved);
        var result = await harness.Sut.ApproveAsync(harness.Request.Id, null, CancellationToken.None);
        result.StatusCode.Should().Be(409);
        result.Error.Should().Contain("already been approved or rejected");
    }

    [Fact]
    public async Task ApproveAsync_WhenCurrentEmployeeIsNotAssigned_ReturnsForbidden()
    {
        var harness = Harness.Create(LeaveRequestStatuses.Pending, otherApprover: true);
        var result = await harness.Sut.ApproveAsync(harness.Request.Id, null, CancellationToken.None);
        result.StatusCode.Should().Be(403);
    }

    [Fact]
    public async Task ApproveAsync_WhenSelfApprovalDisabledAndApproverIsEmployee_ReturnsConflict()
    {
        var harness = Harness.Create(LeaveRequestStatuses.Pending, selfApprove: true);
        var result = await harness.Sut.ApproveAsync(harness.Request.Id, null, CancellationToken.None);
        result.StatusCode.Should().Be(409);
        result.Error.Should().Contain("cannot approve your own leave");
    }

    [Fact]
    public async Task ApproveAsync_WhenAnyOneApproves_MovesPaidHoursFromPendingToUsed()
    {
        var harness = Harness.Create(LeaveRequestStatuses.Pending, paidHours: 3m, pendingHours: 3m, usedHours: 5m);
        var result = await harness.Sut.ApproveAsync(harness.Request.Id, "ok", CancellationToken.None);
        result.IsSuccess.Should().BeTrue();
        harness.Entitlement.PendingHours.Should().Be(0m);
        harness.Entitlement.UsedHours.Should().Be(8m);
        harness.Request.Status.Should().Be(LeaveRequestStatuses.Approved);
        harness.Audits.Should().ContainSingle(a => a.ChangeType == LeaveBalanceChangeTypes.Deduction);
    }

    [Fact]
    public async Task RejectAsync_ReleasesPendingPaidHoursWithoutUsedDeduction()
    {
        var harness = Harness.Create(LeaveRequestStatuses.Pending, paidHours: 2m, pendingHours: 2m, usedHours: 4m);
        var result = await harness.Sut.RejectAsync(harness.Request.Id, "coverage", CancellationToken.None);
        result.IsSuccess.Should().BeTrue();
        harness.Entitlement.PendingHours.Should().Be(0m);
        harness.Entitlement.UsedHours.Should().Be(4m);
        harness.Request.Status.Should().Be(LeaveRequestStatuses.Rejected);
        harness.Audits.Should().BeEmpty();
    }

    [Fact]
    public async Task RequestInfoAsync_PausesRequestAndKeepsPendingBalanceReserved()
    {
        var harness = Harness.Create(LeaveRequestStatuses.Pending, paidHours: 2m, pendingHours: 2m, usedHours: 1m);
        var result = await harness.Sut.RequestInfoAsync(harness.Request.Id, "Need certificate", CancellationToken.None);
        result.IsSuccess.Should().BeTrue();
        harness.Request.Status.Should().Be(LeaveRequestStatuses.InformationRequested);
        harness.Approver.Status.Should().Be(LeaveRequestApproverStatuses.InformationRequested);
        harness.Entitlement.PendingHours.Should().Be(2m);
    }

    [Fact]
    public async Task RespondInfoAsync_ResumesRequestForSameApprover()
    {
        var harness = Harness.Create(LeaveRequestStatuses.InformationRequested, paidHours: 1m, pendingHours: 1m, usedHours: 0m);
        harness.Approver.Status = LeaveRequestApproverStatuses.InformationRequested;
        harness.Employee.Id = harness.Request.EmployeeId;
        var result = await harness.Sut.RespondInfoAsync(harness.Request.Id, "Attached", [], CancellationToken.None);
        result.IsSuccess.Should().BeTrue();
        harness.Request.Status.Should().Be(LeaveRequestStatuses.Pending);
        harness.Approver.Status.Should().Be(LeaveRequestApproverStatuses.Pending);
        harness.InfoMessages.Should().ContainSingle();
    }

    [Fact]
    public async Task ForwardAsync_HandsTheRequestToTheNextApproverAndKeepsItPending()
    {
        var harness = Harness.Create(LeaveRequestStatuses.Pending);
        var managerId = Guid.NewGuid();
        harness.ForwardTarget = managerId;

        var result = await harness.Sut.ForwardAsync(harness.Request.Id, "  Long leave, please decide  ", CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        harness.Request.Status.Should().Be(LeaveRequestStatuses.Pending);
        harness.Approver.Status.Should().Be(LeaveRequestApproverStatuses.Forwarded);
        harness.Approver.Comment.Should().Be("Long leave, please decide");
        harness.Approver.DecidedAt.Should().NotBeNull();
        harness.AddedApprovers.Should().ContainSingle(a =>
            a.ApproverEmployeeId == managerId &&
            a.Status == LeaveRequestApproverStatuses.Pending &&
            a.SequenceOrder == harness.Approver.SequenceOrder &&
            a.LeaveRequestId == harness.Request.Id);
    }

    [Fact]
    public async Task ForwardAsync_WhenNobodyIsAbove_ReturnsConflictAndChangesNothing()
    {
        var harness = Harness.Create(LeaveRequestStatuses.Pending);
        harness.ForwardTarget = null;

        var result = await harness.Sut.ForwardAsync(harness.Request.Id, null, CancellationToken.None);

        result.StatusCode.Should().Be(409);
        harness.Approver.Status.Should().Be(LeaveRequestApproverStatuses.Pending);
        harness.AddedApprovers.Should().BeEmpty();
    }

    [Fact]
    public async Task ForwardAsync_WhenTargetIsTheRequester_ReturnsConflict()
    {
        var harness = Harness.Create(LeaveRequestStatuses.Pending);
        harness.ForwardTarget = harness.Request.EmployeeId;

        var result = await harness.Sut.ForwardAsync(harness.Request.Id, null, CancellationToken.None);

        result.StatusCode.Should().Be(409);
        harness.AddedApprovers.Should().BeEmpty();
    }

    [Fact]
    public async Task ForwardAsync_WhenCallerIsNotTheActionableApprover_ReturnsForbidden()
    {
        var harness = Harness.Create(LeaveRequestStatuses.Pending, otherApprover: true);
        harness.ForwardTarget = Guid.NewGuid();

        var result = await harness.Sut.ForwardAsync(harness.Request.Id, null, CancellationToken.None);

        result.StatusCode.Should().Be(403);
        harness.AddedApprovers.Should().BeEmpty();
    }

    [Fact]
    public async Task ForwardAsync_WhenRequestIsFinal_ReturnsConflict()
    {
        var harness = Harness.Create(LeaveRequestStatuses.Approved);
        harness.ForwardTarget = Guid.NewGuid();

        var result = await harness.Sut.ForwardAsync(harness.Request.Id, null, CancellationToken.None);

        result.StatusCode.Should().Be(409);
        harness.AddedApprovers.Should().BeEmpty();
    }

    [Fact]
    public async Task ChangeDecision_ApprovedToRejected_GivesTheUsedHoursBack()
    {
        var harness = Harness.Create(LeaveRequestStatuses.Approved, paidHours: 8m, pendingHours: 0m, usedHours: 8m);
        harness.Approver.Status = LeaveRequestApproverStatuses.Approved;
        harness.Request.ApprovedBy = harness.Employee.Id;

        var result = await harness.Sut.ChangeDecisionAsync(harness.Request.Id, "reject", "Team is short that week", CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        harness.Request.Status.Should().Be(LeaveRequestStatuses.Rejected);
        harness.Request.ApprovedBy.Should().BeNull();
        harness.Approver.Status.Should().Be(LeaveRequestApproverStatuses.Rejected);
        harness.Approver.Comment.Should().Be("Changed from approved: Team is short that week");
        harness.Entitlement.UsedHours.Should().Be(0m);
        harness.Entitlement.PendingHours.Should().Be(0m);
        harness.Audits.Should().ContainSingle(a => a.ChangeType == LeaveBalanceChangeTypes.Adjustment && a.HoursChanged == 8m);
    }

    [Fact]
    public async Task ChangeDecision_RejectedToApproved_DeductsTheHoursAgain()
    {
        var harness = Harness.Create(LeaveRequestStatuses.Rejected, paidHours: 8m, pendingHours: 0m, usedHours: 4m);
        harness.Approver.Status = LeaveRequestApproverStatuses.Rejected;

        var result = await harness.Sut.ChangeDecisionAsync(harness.Request.Id, "approve", null, CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        harness.Request.Status.Should().Be(LeaveRequestStatuses.Approved);
        harness.Request.ApprovedBy.Should().Be(harness.Employee.Id);
        harness.Approver.Status.Should().Be(LeaveRequestApproverStatuses.Approved);
        harness.Approver.Comment.Should().Be("Changed from rejected");
        harness.Entitlement.UsedHours.Should().Be(12m);
        harness.Audits.Should().ContainSingle(a => a.ChangeType == LeaveBalanceChangeTypes.Deduction && a.HoursChanged == -8m);
    }

    [Fact]
    public async Task ChangeDecision_RejectedToApproved_WhenBalanceNoLongerCoversIt_ReturnsConflict()
    {
        // Entitlement is 20h; 15h already used, so 8h no longer fits.
        var harness = Harness.Create(LeaveRequestStatuses.Rejected, paidHours: 8m, pendingHours: 0m, usedHours: 15m);
        harness.Approver.Status = LeaveRequestApproverStatuses.Rejected;

        var result = await harness.Sut.ChangeDecisionAsync(harness.Request.Id, "approve", null, CancellationToken.None);

        result.StatusCode.Should().Be(409);
        harness.Request.Status.Should().Be(LeaveRequestStatuses.Rejected);
        harness.Entitlement.UsedHours.Should().Be(15m);
    }

    [Fact]
    public async Task ChangeDecision_AfterTheLeaveStarted_ReturnsConflict()
    {
        var harness = Harness.Create(LeaveRequestStatuses.Approved, paidHours: 8m, pendingHours: 0m, usedHours: 8m);
        harness.Approver.Status = LeaveRequestApproverStatuses.Approved;
        harness.Request.ApprovedBy = harness.Employee.Id;
        harness.Request.StartAt = new DateTimeOffset(2026, 8, 22, 9, 0, 0, TimeSpan.Zero);

        var result = await harness.Sut.ChangeDecisionAsync(harness.Request.Id, "reject", "Too late", CancellationToken.None);

        result.StatusCode.Should().Be(409);
        result.Error.Should().Contain("already started");
        harness.Request.Status.Should().Be(LeaveRequestStatuses.Approved);
    }

    [Fact]
    public async Task ChangeDecision_WhenSomeoneElseDecided_ReturnsForbidden()
    {
        var harness = Harness.Create(LeaveRequestStatuses.Approved, otherApprover: true, paidHours: 8m, pendingHours: 0m, usedHours: 8m);
        harness.Approver.Status = LeaveRequestApproverStatuses.Approved;
        harness.Request.ApprovedBy = harness.Approver.ApproverEmployeeId;

        var result = await harness.Sut.ChangeDecisionAsync(harness.Request.Id, "reject", "Not mine", CancellationToken.None);

        result.StatusCode.Should().Be(403);
        harness.Request.Status.Should().Be(LeaveRequestStatuses.Approved);
    }

    [Fact]
    public async Task ChangeDecision_ToTheSameDecision_ReturnsConflict()
    {
        var harness = Harness.Create(LeaveRequestStatuses.Approved, paidHours: 8m, pendingHours: 0m, usedHours: 8m);
        harness.Approver.Status = LeaveRequestApproverStatuses.Approved;
        harness.Request.ApprovedBy = harness.Employee.Id;

        var result = await harness.Sut.ChangeDecisionAsync(harness.Request.Id, "approve", null, CancellationToken.None);

        result.StatusCode.Should().Be(409);
        harness.Entitlement.UsedHours.Should().Be(8m);
    }

    private sealed class Harness
    {
        public LeaveRequest Request { get; }
        public LeaveRequestApprover Approver { get; }
        public LeaveEntitlement Entitlement { get; }
        public Employee Employee { get; }
        public List<LeaveBalanceAudit> Audits { get; } = [];
        public List<LeaveRequestInfoMessage> InfoMessages { get; } = [];
        public List<LeaveRequestApprover> AddedApprovers { get; } = [];
        public Guid? ForwardTarget { get; set; }
        public LeaveApprovalDecisionService Sut { get; }

        private Harness(string status, bool otherApprover, bool selfApprove, decimal paidHours, decimal pendingHours, decimal usedHours)
        {
            var tenantId = Guid.NewGuid();
            var userId = Guid.NewGuid();
            Employee = new Employee
            {
                Id = Guid.NewGuid(),
                TenantId = tenantId,
                UserId = userId,
                FirstName = "Mgr",
                LastName = "One",
                EmployeeNumber = "M1",
                HireDate = new DateOnly(2020, 1, 1)
            };
            var subjectId = selfApprove ? Employee.Id : Guid.NewGuid();
            Request = new LeaveRequest
            {
                Id = Guid.NewGuid(),
                TenantId = tenantId,
                EmployeeId = subjectId,
                LeaveTypeId = Guid.NewGuid(),
                StartAt = new DateTimeOffset(2026, 9, 14, 9, 0, 0, TimeSpan.Zero),
                EndAt = new DateTimeOffset(2026, 9, 14, 18, 0, 0, TimeSpan.Zero),
                TotalHours = paidHours,
                PaidHours = paidHours,
                UnpaidHours = 0m,
                Status = status
            };
            Approver = new LeaveRequestApprover
            {
                Id = Guid.NewGuid(),
                TenantId = tenantId,
                LeaveRequestId = Request.Id,
                ApproverEmployeeId = otherApprover ? Guid.NewGuid() : Employee.Id,
                SequenceOrder = 1,
                Status = LeaveRequestApproverStatuses.Pending
            };
            Entitlement = new LeaveEntitlement
            {
                Id = Guid.NewGuid(),
                TenantId = tenantId,
                EmployeeId = Request.EmployeeId,
                LeaveTypeId = Request.LeaveTypeId,
                Year = 2026,
                TotalHours = 20m,
                UsedHours = usedHours,
                PendingHours = pendingHours,
                CarriedForwardHours = 0m,
                Source = LeaveEntitlementSources.Auto
            };

            var currentUser = new Mock<ICurrentUser>();
            currentUser.SetupGet(x => x.IsAuthenticated).Returns(true);
            currentUser.SetupGet(x => x.TenantId).Returns(tenantId);
            currentUser.SetupGet(x => x.UserId).Returns(userId);
            var clock = new Mock<IDateTimeProvider>();
            clock.SetupGet(x => x.UtcNow).Returns(new DateTimeOffset(2026, 8, 22, 10, 0, 0, TimeSpan.Zero));
            var employees = new Mock<IEmployeeRepository>();
            employees.Setup(x => x.GetByUserIdAsync(tenantId, userId, It.IsAny<CancellationToken>())).ReturnsAsync(Employee);
            employees.Setup(x => x.GetByIdAsync(tenantId, It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
                .Returns((Guid _, Guid id, CancellationToken _) => Task.FromResult<Employee?>(new Employee
                {
                    Id = id,
                    UserId = Guid.NewGuid(),
                    TenantId = tenantId,
                    FirstName = "A",
                    LastName = "B",
                    EmployeeNumber = "A1",
                    HireDate = new DateOnly(2020, 1, 1)
                }));

            var repo = new Mock<ILeaveApprovalRepository>();
            repo.Setup(x => x.GetStateAsync(tenantId, Request.Id, It.IsAny<CancellationToken>()))
                .ReturnsAsync(() => new LeaveApprovalState(
                    Request, Entitlement, new Employee
                    {
                        Id = Request.EmployeeId,
                        TenantId = tenantId,
                        UserId = Guid.NewGuid(),
                        FirstName = "Priya",
                        LastName = "Nair",
                        HireDate = new DateOnly(2024, 1, 1)
                    },
                    "Annual Leave", "AL", LeaveApprovalModes.AnyOne, [Approver], InfoMessages));
            repo.Setup(x => x.AddBalanceAuditAsync(It.IsAny<LeaveBalanceAudit>(), It.IsAny<CancellationToken>()))
                .Callback<LeaveBalanceAudit, CancellationToken>((a, _) => Audits.Add(a))
                .Returns(Task.CompletedTask);
            repo.Setup(x => x.AddInfoMessageAsync(It.IsAny<LeaveRequestInfoMessage>(), It.IsAny<CancellationToken>()))
                .Callback<LeaveRequestInfoMessage, CancellationToken>((m, _) => InfoMessages.Add(m))
                .Returns(Task.CompletedTask);
            repo.Setup(x => x.AreAvailableFileRecordsAsync(tenantId, It.IsAny<IReadOnlyCollection<Guid>>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(true);
            repo.Setup(x => x.AddApproverAsync(It.IsAny<LeaveRequestApprover>(), It.IsAny<CancellationToken>()))
                .Callback<LeaveRequestApprover, CancellationToken>((a, _) => AddedApprovers.Add(a))
                .Returns(Task.CompletedTask);
            repo.Setup(x => x.SaveChangesAsync(It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);

            var outbox = new Mock<IOutboxWriter>();
            outbox.Setup(x => x.EnqueueAsync(
                    It.IsAny<string>(),
                    It.IsAny<LeaveRequestApprovedPayload>(),
                    It.IsAny<Guid?>(),
                    It.IsAny<CancellationToken>()))
                .Returns(Task.CompletedTask);
            outbox.Setup(x => x.EnqueueAsync(
                    It.IsAny<string>(),
                    It.IsAny<LeaveRequestRejectedPayload>(),
                    It.IsAny<Guid?>(),
                    It.IsAny<CancellationToken>()))
                .Returns(Task.CompletedTask);
            outbox.Setup(x => x.EnqueueAsync(
                    It.IsAny<string>(),
                    It.IsAny<LeaveInformationRequestedPayload>(),
                    It.IsAny<Guid?>(),
                    It.IsAny<CancellationToken>()))
                .Returns(Task.CompletedTask);
            var notifications = new Mock<INotificationDispatcher>();
            notifications.Setup(x => x.SendTemplatedAsync(It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<IReadOnlyDictionary<string, string>>(), It.IsAny<string?>(), It.IsAny<Guid?>(), It.IsAny<CancellationToken>()))
                .Returns(Task.CompletedTask);
            var conflicts = new Mock<ILeaveRequestConflictProvider>();
            conflicts.Setup(x => x.ListConflictsAsync(It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<DateOnly>(), It.IsAny<DateOnly>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync([]);

            var approverResolver = new Mock<ILeaveApproverResolver>();
            approverResolver.Setup(x => x.ResolveAsync(tenantId, Employee.Id, It.IsAny<DateOnly>(), It.IsAny<DateOnly>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(() => new LeaveApproverResolution(ForwardTarget is { } target
                    ? [new LeaveApproverResolutionRow(target, 1, null)]
                    : []));

            Sut = new LeaveApprovalDecisionService(
                currentUser.Object, clock.Object, employees.Object, repo.Object,
                outbox.Object, notifications.Object, conflicts.Object,
                new LeaveForwardTargetResolver(approverResolver.Object),
                Options.Create(new LeaveApprovalOptions { AllowSelfApproval = false }));
        }

        public static Harness Create(
            string status,
            bool otherApprover = false,
            bool selfApprove = false,
            decimal paidHours = 1m,
            decimal pendingHours = 1m,
            decimal usedHours = 0m) =>
            new(status, otherApprover, selfApprove, paidHours, pendingHours, usedHours);
    }
}
