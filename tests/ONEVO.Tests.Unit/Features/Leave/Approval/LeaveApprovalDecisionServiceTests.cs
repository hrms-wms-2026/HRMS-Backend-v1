using FluentAssertions;
using Microsoft.Extensions.Options;
using Moq;
using ONEVO.Application.Common.RepositoryInterfaces;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.Leave.Approval.Commands;
using ONEVO.Application.Features.Leave.Approval.Options;
using ONEVO.Application.Features.Leave.Approval.OutboxHandlers;
using ONEVO.Application.Features.Leave.Approval.RepositoryInterfaces;
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
    public async Task ApproveAsync_WhenAnyOneApproves_MovesPaidDaysFromPendingToUsed()
    {
        var harness = Harness.Create(LeaveRequestStatuses.Pending, paidDays: 3m, pendingDays: 3m, usedDays: 5m);
        var result = await harness.Sut.ApproveAsync(harness.Request.Id, "ok", CancellationToken.None);
        result.IsSuccess.Should().BeTrue();
        harness.Entitlement.PendingDays.Should().Be(0m);
        harness.Entitlement.UsedDays.Should().Be(8m);
        harness.Request.Status.Should().Be(LeaveRequestStatuses.Approved);
        harness.Audits.Should().ContainSingle(a => a.ChangeType == LeaveBalanceChangeTypes.Deduction);
    }

    [Fact]
    public async Task RejectAsync_ReleasesPendingPaidDaysWithoutUsedDeduction()
    {
        var harness = Harness.Create(LeaveRequestStatuses.Pending, paidDays: 2m, pendingDays: 2m, usedDays: 4m);
        var result = await harness.Sut.RejectAsync(harness.Request.Id, "coverage", CancellationToken.None);
        result.IsSuccess.Should().BeTrue();
        harness.Entitlement.PendingDays.Should().Be(0m);
        harness.Entitlement.UsedDays.Should().Be(4m);
        harness.Request.Status.Should().Be(LeaveRequestStatuses.Rejected);
        harness.Audits.Should().BeEmpty();
    }

    [Fact]
    public async Task RequestInfoAsync_PausesRequestAndKeepsPendingBalanceReserved()
    {
        var harness = Harness.Create(LeaveRequestStatuses.Pending, paidDays: 2m, pendingDays: 2m, usedDays: 1m);
        var result = await harness.Sut.RequestInfoAsync(harness.Request.Id, "Need certificate", CancellationToken.None);
        result.IsSuccess.Should().BeTrue();
        harness.Request.Status.Should().Be(LeaveRequestStatuses.InformationRequested);
        harness.Approver.Status.Should().Be(LeaveRequestApproverStatuses.InformationRequested);
        harness.Entitlement.PendingDays.Should().Be(2m);
    }

    [Fact]
    public async Task RespondInfoAsync_ResumesRequestForSameApprover()
    {
        var harness = Harness.Create(LeaveRequestStatuses.InformationRequested, paidDays: 1m, pendingDays: 1m, usedDays: 0m);
        harness.Approver.Status = LeaveRequestApproverStatuses.InformationRequested;
        harness.Employee.Id = harness.Request.EmployeeId;
        var result = await harness.Sut.RespondInfoAsync(harness.Request.Id, "Attached", [], CancellationToken.None);
        result.IsSuccess.Should().BeTrue();
        harness.Request.Status.Should().Be(LeaveRequestStatuses.Pending);
        harness.Approver.Status.Should().Be(LeaveRequestApproverStatuses.Pending);
        harness.InfoMessages.Should().ContainSingle();
    }

    private sealed class Harness
    {
        public LeaveRequest Request { get; }
        public LeaveRequestApprover Approver { get; }
        public LeaveEntitlement Entitlement { get; }
        public Employee Employee { get; }
        public List<LeaveBalanceAudit> Audits { get; } = [];
        public List<LeaveRequestInfoMessage> InfoMessages { get; } = [];
        public LeaveApprovalDecisionService Sut { get; }

        private Harness(string status, bool otherApprover, bool selfApprove, decimal paidDays, decimal pendingDays, decimal usedDays)
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
                StartDate = new DateOnly(2026, 9, 14),
                EndDate = new DateOnly(2026, 9, 14),
                TotalDays = paidDays,
                PaidDays = paidDays,
                UnpaidDays = 0m,
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
                TotalDays = 20m,
                UsedDays = usedDays,
                PendingDays = pendingDays,
                CarriedForwardDays = 0m,
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

            Sut = new LeaveApprovalDecisionService(
                currentUser.Object, clock.Object, employees.Object, repo.Object,
                outbox.Object, notifications.Object, conflicts.Object,
                Options.Create(new LeaveApprovalOptions { AllowSelfApproval = false }));
        }

        public static Harness Create(
            string status,
            bool otherApprover = false,
            bool selfApprove = false,
            decimal paidDays = 1m,
            decimal pendingDays = 1m,
            decimal usedDays = 0m) =>
            new(status, otherApprover, selfApprove, paidDays, pendingDays, usedDays);
    }
}
