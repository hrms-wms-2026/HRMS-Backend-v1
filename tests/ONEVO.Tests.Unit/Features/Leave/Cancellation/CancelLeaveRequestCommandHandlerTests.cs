using FluentAssertions;
using Microsoft.Extensions.Options;
using Moq;
using ONEVO.Application.Common.Exceptions;
using ONEVO.Application.Common.RepositoryInterfaces;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.Leave.Cancellation.Commands;
using ONEVO.Application.Features.Leave.Cancellation.Helpers;
using ONEVO.Application.Features.Leave.Cancellation.Options;
using ONEVO.Application.Features.Leave.Cancellation.Outbox;
using ONEVO.Application.Features.Leave.Cancellation.RepositoryInterfaces;
using ONEVO.Application.Features.Leave.Policy.RepositoryInterfaces;
using ONEVO.Application.Features.Leave.Request.Helpers;
using ONEVO.Application.Features.Leave.Request.Services;
using ONEVO.Domain.Features.CoreHr.Entities;
using ONEVO.Domain.Features.Leave.BalanceAudit.Entities;
using ONEVO.Domain.Features.Leave.Common;
using ONEVO.Domain.Features.Leave.Entitlement.Entities;
using ONEVO.Domain.Features.Leave.Request.Entities;
using ONEVO.Domain.Features.OrgStructure.Entities;
using Xunit;

namespace ONEVO.Tests.Unit.Features.Leave.Cancellation;

public class CancelLeaveRequestCommandHandlerTests
{
    [Fact]
    public async Task Handle_EmployeeCancelsOwnPending_ReleasesPendingWithoutAudit()
    {
        var harness = Harness.Create(LeaveRequestStatuses.Pending, paidDays: 2m, pendingDays: 2m, usedDays: 4m);
        var result = await harness.Sut.Handle(new CancelLeaveRequestCommand(harness.Request.Id, null, null, null), CancellationToken.None);
        result.IsSuccess.Should().BeTrue();
        harness.Entitlement.PendingDays.Should().Be(0m);
        harness.Entitlement.UsedDays.Should().Be(4m);
        harness.Request.Status.Should().Be(LeaveRequestStatuses.Cancelled);
        harness.Audits.Should().BeEmpty();
        harness.Approver.Status.Should().Be(LeaveRequestApproverStatuses.Cancelled);
    }

    [Fact]
    public async Task Handle_EmployeeCancelsInformationRequested_MarksApproverCancelled()
    {
        var harness = Harness.Create(LeaveRequestStatuses.InformationRequested, paidDays: 1m, pendingDays: 1m, usedDays: 0m);
        harness.Approver.Status = LeaveRequestApproverStatuses.InformationRequested;
        var result = await harness.Sut.Handle(new CancelLeaveRequestCommand(harness.Request.Id, null, null, null), CancellationToken.None);
        result.IsSuccess.Should().BeTrue();
        harness.Approver.Status.Should().Be(LeaveRequestApproverStatuses.Cancelled);
        harness.Audits.Should().BeEmpty();
    }

    [Fact]
    public async Task Handle_EmployeeCannotCancelAnotherEmployeesRequest()
    {
        var harness = Harness.Create(LeaveRequestStatuses.Pending, ownerIsCaller: false);
        var result = await harness.Sut.Handle(new CancelLeaveRequestCommand(harness.Request.Id, "coverage", null, null), CancellationToken.None);
        result.StatusCode.Should().Be(403);
        result.Error.Should().Be(LeaveCancellationMessages.NotOwner);
    }

    [Fact]
    public async Task Handle_HrCancelWithoutReason_Fails()
    {
        var harness = Harness.Create(LeaveRequestStatuses.Pending, ownerIsCaller: false, hrPermission: true);
        var result = await harness.Sut.Handle(new CancelLeaveRequestCommand(harness.Request.Id, "  ", null, null), CancellationToken.None);
        result.IsSuccess.Should().BeFalse();
        result.Error.Should().Be(LeaveCancellationMessages.HrReasonRequired);
    }

    [Fact]
    public async Task Handle_HrCancelNotifiesEmployee()
    {
        var harness = Harness.Create(LeaveRequestStatuses.Pending, ownerIsCaller: false, hrPermission: true);
        var result = await harness.Sut.Handle(new CancelLeaveRequestCommand(harness.Request.Id, "coverage", null, null), CancellationToken.None);
        result.IsSuccess.Should().BeTrue();
        harness.Notifications.Verify(x => x.SendTemplatedAsync(
            harness.TenantId, harness.Subject.UserId, "leave_request_cancelled_by_hr",
            It.IsAny<IReadOnlyDictionary<string, string>>(), "leave_request", harness.Request.Id, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Handle_AlreadyCancelled_ReturnsProductMessage()
    {
        var harness = Harness.Create(LeaveRequestStatuses.Cancelled);
        var result = await harness.Sut.Handle(new CancelLeaveRequestCommand(harness.Request.Id, null, null, null), CancellationToken.None);
        result.Error.Should().Be(LeaveCancellationMessages.AlreadyCancelled);
    }

    [Fact]
    public async Task Handle_Rejected_ReturnsProductMessage()
    {
        var harness = Harness.Create(LeaveRequestStatuses.Rejected);
        var result = await harness.Sut.Handle(new CancelLeaveRequestCommand(harness.Request.Id, null, null, null), CancellationToken.None);
        result.Error.Should().Be(LeaveCancellationMessages.Rejected);
    }

    [Fact]
    public async Task Handle_FullyPassed_ReturnsProductMessage()
    {
        var harness = Harness.Create(
            LeaveRequestStatuses.Approved,
            start: new DateOnly(2026, 8, 1),
            end: new DateOnly(2026, 8, 3),
            businessDate: new DateOnly(2026, 8, 22));
        var result = await harness.Sut.Handle(new CancelLeaveRequestCommand(harness.Request.Id, null, null, null), CancellationToken.None);
        result.Error.Should().Be(LeaveCancellationMessages.PeriodPassed);
    }

    [Fact]
    public async Task Handle_ApprovedFuture_RestoresUsedDaysAndWritesAdjustment()
    {
        var harness = Harness.Create(
            LeaveRequestStatuses.Approved,
            paidDays: 3m,
            pendingDays: 0m,
            usedDays: 8m,
            start: new DateOnly(2026, 9, 14),
            end: new DateOnly(2026, 9, 16),
            businessDate: new DateOnly(2026, 8, 22));
        var result = await harness.Sut.Handle(new CancelLeaveRequestCommand(harness.Request.Id, null, null, null), CancellationToken.None);
        result.IsSuccess.Should().BeTrue();
        harness.Entitlement.UsedDays.Should().Be(5m);
        harness.Audits.Should().ContainSingle(a => a.ChangeType == LeaveBalanceChangeTypes.Adjustment && a.DaysChanged == 3m);
        harness.OutboxTypes.Should().Contain(OutboxMessageTypes.LeaveRequestCancelled);
    }

    [Fact]
    public async Task Handle_ApprovedInProgress_RestoresOnlyFuturePaidAllocations()
    {
        var harness = Harness.Create(
            LeaveRequestStatuses.Approved,
            paidDays: 3m,
            pendingDays: 0m,
            usedDays: 3m,
            start: new DateOnly(2026, 8, 20),
            end: new DateOnly(2026, 8, 22),
            businessDate: new DateOnly(2026, 8, 21));
        harness.Allocations.AddRange(
        [
            Allocation(harness, new DateOnly(2026, 8, 20), 1m),
            Allocation(harness, new DateOnly(2026, 8, 21), 1m),
            Allocation(harness, new DateOnly(2026, 8, 22), 1m)
        ]);
        var result = await harness.Sut.Handle(new CancelLeaveRequestCommand(harness.Request.Id, null, null, null), CancellationToken.None);
        result.IsSuccess.Should().BeTrue();
        result.Value!.IsPartialCancellation.Should().BeTrue();
        result.Value.RestoredUsedDays.Should().Be(2m);
        harness.Entitlement.UsedDays.Should().Be(1m);
        harness.Allocations.Count(a => a.Status == LeaveRequestDayAllocationStatuses.Cancelled).Should().Be(2);
        harness.Allocations.Single(a => a.LeaveDate == new DateOnly(2026, 8, 20)).Status.Should().Be(LeaveRequestDayAllocationStatuses.Active);
    }

    [Fact]
    public async Task Handle_PartialUnpaidOnlyFutureDays_DoesNotWriteZeroAudit()
    {
        var harness = Harness.Create(
            LeaveRequestStatuses.Approved,
            paidDays: 1m,
            pendingDays: 0m,
            usedDays: 1m,
            start: new DateOnly(2026, 8, 20),
            end: new DateOnly(2026, 8, 21),
            businessDate: new DateOnly(2026, 8, 21));
        harness.Allocations.AddRange(
        [
            Allocation(harness, new DateOnly(2026, 8, 20), 1m),
            new LeaveRequestDayAllocation
            {
                Id = Guid.NewGuid(),
                TenantId = harness.TenantId,
                LeaveRequestId = harness.Request.Id,
                LeaveDate = new DateOnly(2026, 8, 21),
                DayUnit = 1m,
                PaidUnit = 0m,
                UnpaidUnit = 1m,
                Status = LeaveRequestDayAllocationStatuses.Active
            }
        ]);
        var result = await harness.Sut.Handle(new CancelLeaveRequestCommand(harness.Request.Id, null, null, null), CancellationToken.None);
        result.IsSuccess.Should().BeTrue();
        harness.Audits.Should().BeEmpty();
        harness.OutboxTypes.Should().Contain(OutboxMessageTypes.LeaveRequestCancelled);
    }

    [Fact]
    public async Task Handle_StaleVersion_ReturnsConcurrencyMessage()
    {
        var harness = Harness.Create(LeaveRequestStatuses.Pending);
        harness.ThrowConcurrency = true;
        var result = await harness.Sut.Handle(new CancelLeaveRequestCommand(harness.Request.Id, null, null, "9"), CancellationToken.None);
        result.Error.Should().Be(LeaveCancellationMessages.Concurrency);
    }

    [Fact]
    public async Task Handle_EmployeeApprovedCancel_NotifiesApprovers()
    {
        var harness = Harness.Create(
            LeaveRequestStatuses.Approved,
            start: new DateOnly(2026, 9, 14),
            end: new DateOnly(2026, 9, 14),
            businessDate: new DateOnly(2026, 8, 22));
        harness.Approver.Status = LeaveRequestApproverStatuses.Approved;
        var result = await harness.Sut.Handle(new CancelLeaveRequestCommand(harness.Request.Id, null, null, null), CancellationToken.None);
        result.IsSuccess.Should().BeTrue();
        harness.Notifications.Verify(x => x.SendTemplatedAsync(
            harness.TenantId, harness.ApproverUserId, "leave_request_cancelled_by_employee",
            It.IsAny<IReadOnlyDictionary<string, string>>(), "leave_request", harness.Request.Id, It.IsAny<CancellationToken>()), Times.Once);
    }

    private static LeaveRequestDayAllocation Allocation(Harness harness, DateOnly date, decimal paid) => new()
    {
        Id = Guid.NewGuid(),
        TenantId = harness.TenantId,
        LeaveRequestId = harness.Request.Id,
        LeaveDate = date,
        DayUnit = 1m,
        PaidUnit = paid,
        UnpaidUnit = 0m,
        Status = LeaveRequestDayAllocationStatuses.Active
    };

    private sealed class Harness
    {
        public Guid TenantId { get; }
        public LeaveRequest Request { get; }
        public LeaveRequestApprover Approver { get; }
        public LeaveEntitlement Entitlement { get; }
        public Employee Caller { get; }
        public Employee Subject { get; }
        public Guid ApproverUserId { get; } = Guid.NewGuid();
        public List<LeaveBalanceAudit> Audits { get; } = [];
        public List<LeaveRequestDayAllocation> Allocations { get; } = [];
        public List<string> OutboxTypes { get; } = [];
        public Mock<INotificationDispatcher> Notifications { get; } = new();
        public bool ThrowConcurrency { get; set; }
        public CancelLeaveRequestCommandHandler Sut { get; }

        private Harness(
            string status,
            bool ownerIsCaller,
            bool hrPermission,
            decimal paidDays,
            decimal pendingDays,
            decimal usedDays,
            DateOnly start,
            DateOnly end,
            DateOnly businessDate)
        {
            TenantId = Guid.NewGuid();
            var userId = Guid.NewGuid();
            Caller = new Employee
            {
                Id = Guid.NewGuid(),
                TenantId = TenantId,
                UserId = userId,
                FirstName = "Mgr",
                LastName = "One",
                EmployeeNumber = "M1",
                HireDate = new DateOnly(2020, 1, 1),
                LegalEntityId = Guid.NewGuid()
            };
            Subject = ownerIsCaller
                ? Caller
                : new Employee
                {
                    Id = Guid.NewGuid(),
                    TenantId = TenantId,
                    UserId = Guid.NewGuid(),
                    FirstName = "Priya",
                    LastName = "Nair",
                    EmployeeNumber = "E1",
                    HireDate = new DateOnly(2024, 1, 1),
                    LegalEntityId = Caller.LegalEntityId
                };
            Request = new LeaveRequest
            {
                Id = Guid.NewGuid(),
                TenantId = TenantId,
                EmployeeId = Subject.Id,
                LeaveTypeId = Guid.NewGuid(),
                StartDate = start,
                EndDate = end,
                TotalDays = paidDays,
                PaidDays = paidDays,
                UnpaidDays = 0m,
                Status = status
            };
            Approver = new LeaveRequestApprover
            {
                Id = Guid.NewGuid(),
                TenantId = TenantId,
                LeaveRequestId = Request.Id,
                ApproverEmployeeId = Guid.NewGuid(),
                SequenceOrder = 1,
                Status = LeaveRequestApproverStatuses.Pending
            };
            Entitlement = new LeaveEntitlement
            {
                Id = Guid.NewGuid(),
                TenantId = TenantId,
                EmployeeId = Request.EmployeeId,
                LeaveTypeId = Request.LeaveTypeId,
                Year = start.Year,
                TotalDays = 20m,
                UsedDays = usedDays,
                PendingDays = pendingDays,
                CarriedForwardDays = 0m,
                Source = LeaveEntitlementSources.Auto
            };

            var currentUser = new Mock<ICurrentUser>();
            currentUser.SetupGet(x => x.IsAuthenticated).Returns(true);
            currentUser.SetupGet(x => x.TenantId).Returns(TenantId);
            currentUser.SetupGet(x => x.UserId).Returns(userId);
            currentUser.Setup(x => x.HasPermission("leave:manage")).Returns(hrPermission);

            var employees = new Mock<IEmployeeRepository>();
            employees.Setup(x => x.GetByUserIdAsync(TenantId, userId, It.IsAny<CancellationToken>())).ReturnsAsync(Caller);

            var repo = new Mock<ILeaveCancellationRepository>();
            repo.Setup(x => x.GetStateAsync(TenantId, Request.Id, It.IsAny<CancellationToken>()))
                .ReturnsAsync(() => new LeaveCancellationState(
                    Request,
                    Entitlement,
                    Subject,
                    new LegalEntity { Id = Subject.LegalEntityId!.Value, TenantId = TenantId, Name = "Acme", Timezone = "UTC", CountryCode = "LKA", CurrencyCode = "LKR" },
                    "Annual Leave",
                    "AL",
                    [Approver],
                    [new LeaveCancellationRecipient(Approver.ApproverEmployeeId, ApproverUserId, "Boss One")]));
            repo.Setup(x => x.ListAllocationsAsync(TenantId, Request.Id, It.IsAny<CancellationToken>()))
                .ReturnsAsync(() => Allocations);
            repo.Setup(x => x.AddBalanceAuditAsync(It.IsAny<LeaveBalanceAudit>(), It.IsAny<CancellationToken>()))
                .Callback<LeaveBalanceAudit, CancellationToken>((a, _) => Audits.Add(a))
                .Returns(Task.CompletedTask);
            repo.Setup(x => x.AddAllocationsAsync(It.IsAny<IReadOnlyList<LeaveRequestDayAllocation>>(), It.IsAny<CancellationToken>()))
                .Callback<IReadOnlyList<LeaveRequestDayAllocation>, CancellationToken>((rows, _) => Allocations.AddRange(rows))
                .Returns(Task.CompletedTask);
            repo.Setup(x => x.SaveChangesAsync(It.IsAny<CancellationToken>()))
                .Returns(() => ThrowConcurrency ? Task.FromException(new ConcurrencyConflictException()) : Task.CompletedTask);

            var clock = new Mock<IDateTimeProvider>();
            clock.SetupGet(x => x.UtcNow).Returns(new DateTimeOffset(businessDate, TimeOnly.Parse("10:00"), TimeSpan.Zero));
            clock.SetupGet(x => x.Today).Returns(businessDate);

            var outbox = new Mock<IOutboxWriter>();
            outbox.Setup(x => x.EnqueueAsync(
                    It.IsAny<string>(),
                    It.IsAny<LeaveRequestCancelledPayload>(),
                    It.IsAny<Guid?>(),
                    It.IsAny<CancellationToken>()))
                .Callback<string, LeaveRequestCancelledPayload, Guid?, CancellationToken>((type, _, _, _) => OutboxTypes.Add(type))
                .Returns(Task.CompletedTask);
            Notifications.Setup(x => x.SendTemplatedAsync(
                    It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<string>(),
                    It.IsAny<IReadOnlyDictionary<string, string>>(), It.IsAny<string?>(), It.IsAny<Guid?>(), It.IsAny<CancellationToken>()))
                .Returns(Task.CompletedTask);

            var holidays = new Mock<ILeaveHolidayProvider>();
            holidays.Setup(x => x.ListHolidaysAsync(It.IsAny<Guid>(), It.IsAny<Guid?>(), It.IsAny<DateOnly>(), It.IsAny<DateOnly>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync([]);
            var policies = new Mock<ILeavePolicyRepository>();
            policies.Setup(x => x.ListActiveAggregatesByLegalEntityIdsAsync(
                    It.IsAny<Guid>(), It.IsAny<IReadOnlyCollection<Guid>>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(new Dictionary<Guid, LeavePolicyAggregate>());

            Sut = new CancelLeaveRequestCommandHandler(
                currentUser.Object,
                employees.Object,
                repo.Object,
                new LeaveBusinessDateResolver(clock.Object, Options.Create(new LeaveCancellationOptions { FallbackTimezone = "UTC" })),
                new LeaveCancellationClassifier(),
                new LeaveRequestDayAllocationBuilder(),
                new LeaveRequestDayCalculator(),
                holidays.Object,
                policies.Object,
                outbox.Object,
                Notifications.Object,
                clock.Object,
                Options.Create(new LeaveCancellationOptions { FallbackTimezone = "UTC" }));
        }

        public static Harness Create(
            string status,
            bool ownerIsCaller = true,
            bool hrPermission = false,
            decimal paidDays = 1m,
            decimal pendingDays = 1m,
            decimal usedDays = 0m,
            DateOnly? start = null,
            DateOnly? end = null,
            DateOnly? businessDate = null)
            => new(
                status,
                ownerIsCaller,
                hrPermission,
                paidDays,
                pendingDays,
                usedDays,
                start ?? new DateOnly(2026, 9, 14),
                end ?? new DateOnly(2026, 9, 14),
                businessDate ?? new DateOnly(2026, 8, 22));
    }
}
