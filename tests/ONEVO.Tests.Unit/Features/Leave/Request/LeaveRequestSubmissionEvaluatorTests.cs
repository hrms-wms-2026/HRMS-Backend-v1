using FluentAssertions;
using Microsoft.Extensions.Options;
using Moq;
using ONEVO.Application.Common.RepositoryInterfaces;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.Leave.Calendar.Services;
using ONEVO.Application.Features.Leave.Entitlement.Helpers;
using ONEVO.Application.Features.Leave.Entitlement.RepositoryInterfaces;
using ONEVO.Application.Features.Leave.Policy.RepositoryInterfaces;
using ONEVO.Application.Features.Leave.Policy.Services;
using ONEVO.Application.Features.Leave.Request.Helpers;
using ONEVO.Application.Features.Leave.Request.Options;
using ONEVO.Application.Features.Leave.Request.RepositoryInterfaces;
using ONEVO.Application.Features.Leave.Request.Services;
using ONEVO.Application.Features.Leave.Type.RepositoryInterfaces;

using ONEVO.Domain.Features.CoreHr.Entities;
using ONEVO.Domain.Features.Leave.Common;
using ONEVO.Domain.Features.Leave.Entitlement.Entities;
using ONEVO.Domain.Features.Leave.Policy.Entities;
using ONEVO.Domain.Features.Leave.Type.Entities;
using ONEVO.Tests.Unit.Features.Leave.Entitlement;
using Xunit;

namespace ONEVO.Tests.Unit.Features.Leave.Request;

public class LeaveRequestSubmissionEvaluatorTests
{
    [Fact]
    public async Task Evaluate_WhenBackdatedAndDisallowed_Fails()
    {
        var harness = Harness.Create(allowBackdated: false);
        harness.Clock.SetupGet(x => x.Today).Returns(new DateOnly(2026, 8, 21));

        var result = await harness.Sut.EvaluateAsync(
            harness.TenantId, harness.UserId, null, harness.LeaveTypeId,
            At(2026, 8, 10, 9), At(2026, 8, 10, 18), null, [], CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.Error.Should().Be(LeaveRequestMessages.StartInPast);
    }

    [Fact]
    public async Task Evaluate_WhenOverlapExists_ReturnsConflict()
    {
        var harness = Harness.Create();
        harness.Requests.Setup(x => x.HasOverlappingPendingOrApprovedRequestAsync(
                harness.TenantId, harness.Employee.Id, It.IsAny<DateTimeOffset>(), It.IsAny<DateTimeOffset>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        var result = await harness.EvaluateDefaultAsync();

        result.IsSuccess.Should().BeFalse();
        result.StatusCode.Should().Be(409);
        result.Error.Should().Be(LeaveRequestMessages.Overlap);
    }

    [Fact]
    public async Task Evaluate_OverlapCheckUsesOriginalStartAtAndEndAt()
    {
        var harness = Harness.Create();
        var start = new DateTimeOffset(2026, 8, 21, 22, 0, 0, TimeSpan.FromHours(8));
        var end = new DateTimeOffset(2026, 8, 22, 6, 0, 0, TimeSpan.FromHours(8));
        DateTimeOffset? passedStart = null;
        DateTimeOffset? passedEnd = null;
        harness.Requests.Setup(x => x.HasOverlappingPendingOrApprovedRequestAsync(
                harness.TenantId, harness.Employee.Id, It.IsAny<DateTimeOffset>(), It.IsAny<DateTimeOffset>(), It.IsAny<CancellationToken>()))
            .Callback<Guid, Guid, DateTimeOffset, DateTimeOffset, CancellationToken>((_, _, s, e, _) =>
            {
                passedStart = s;
                passedEnd = e;
            })
            .ReturnsAsync(false);

        await harness.Sut.EvaluateAsync(
            harness.TenantId, harness.UserId, null, harness.LeaveTypeId,
            start, end, null, [], CancellationToken.None);

        passedStart.Should().Be(start);
        passedEnd.Should().Be(end);
    }

    [Fact]
    public async Task Evaluate_WhenBalanceShort_AllowsPaidRequestAndFlagsExcess()
    {
        var harness = Harness.Create(remainingHours: 8m, allowUnpaid: false);
        var result = await harness.EvaluateDefaultAsync(end: At(2026, 8, 20, 18));

        result.IsSuccess.Should().BeTrue();
        result.Value!.PaidHours.Should().Be(24m);
        result.Value.UnpaidHours.Should().Be(0m);
        result.Value.ExceedsBalanceByHours.Should().Be(16m);
    }

    [Fact]
    public async Task Evaluate_LegacyUnpaidSplitFlag_DoesNotSplitPaidLeave()
    {
        var harness = Harness.Create(remainingHours: 8m, allowUnpaid: true);
        var result = await harness.EvaluateDefaultAsync(end: At(2026, 8, 20, 18));

        result.IsSuccess.Should().BeTrue();
        result.Value!.PaidHours.Should().Be(24m);
        result.Value.UnpaidHours.Should().Be(0m);
        result.Value.ExceedsBalanceByHours.Should().Be(16m);
    }

    [Fact]
    public async Task Missing_entitlement_row_counts_as_zero_available_for_paid_leave()
    {
        var harness = Harness.Create();
        harness.Entitlements.Setup(x => x.GetTrackedByEmployeeTypeYearAsync(
                harness.TenantId, harness.Employee.Id, harness.LeaveTypeId, 2026, It.IsAny<CancellationToken>()))
            .ReturnsAsync((LeaveEntitlement?)null);

        var result = await harness.EvaluateDefaultAsync();

        result.IsSuccess.Should().BeTrue(result.Error);
        result.Value!.PaidHours.Should().Be(8m);
        result.Value.ExceedsBalanceByHours.Should().Be(8m);
        result.Value.Entitlement.TotalHours.Should().Be(0m);
        result.Value.Entitlement.Source.Should().Be(LeaveEntitlementSources.Auto);
    }

    [Fact]
    public async Task Unpaid_type_never_flags_an_over_balance_request()
    {
        var harness = Harness.Create(remainingHours: 0m);
        harness.LeaveType.IsPaid = false;

        var result = await harness.EvaluateDefaultAsync();

        result.IsSuccess.Should().BeTrue(result.Error);
        result.Value!.PaidHours.Should().Be(0m);
        result.Value.UnpaidHours.Should().Be(8m);
        result.Value.ExceedsBalanceByHours.Should().Be(0m);
    }

    [Fact]
    public async Task Approval_mode_none_does_not_require_an_approver()
    {
        var harness = Harness.Create();
        harness.Policy.ApprovalMode = LeaveApprovalModes.None;
        harness.Approvers.Setup(x => x.ResolveAsync(harness.TenantId, harness.Employee.Id,
                It.IsAny<DateOnly>(), It.IsAny<DateOnly>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new LeaveApproverResolution([]));

        var result = await harness.EvaluateDefaultAsync();

        result.IsSuccess.Should().BeTrue(result.Error);
        result.Value!.ApprovalMode.Should().Be(LeaveApprovalModes.None);
        result.Value.Approvers.Approvers.Should().BeEmpty();
    }

    [Fact]
    public async Task Evaluate_WhenNoApprover_Fails()
    {
        var harness = Harness.Create();
        harness.Approvers.Setup(x => x.ResolveAsync(harness.TenantId, harness.Employee.Id, It.IsAny<DateOnly>(), It.IsAny<DateOnly>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new LeaveApproverResolution([]));

        var result = await harness.EvaluateDefaultAsync();

        result.IsSuccess.Should().BeFalse();
        result.Error.Should().Be(LeaveRequestMessages.NoApprover);
    }

    [Fact]
    public async Task Evaluate_WhenNoticePeriodMissed_SucceedsWithWarning()
    {
        var harness = Harness.Create();
        harness.Policy.MinimumNoticeDays = 10;
        var result = await harness.EvaluateDefaultAsync();

        result.IsSuccess.Should().BeTrue();
        result.Value!.NoticePeriodMissed.Should().BeTrue();
        result.Value.Warnings.Should().Contain(w => w.Code == "notice_period_missed");
    }

    [Fact]
    public async Task No_policy_rejects()
    {
        var harness = Harness.Create();
        harness.PolicyResolved = false;
        var result = await harness.EvaluateDefaultAsync();

        result.IsSuccess.Should().BeFalse();
        result.Error.Should().Be(LeaveRequestMessages.NoEntitlement);
    }

    [Fact]
    public async Task Min_request_hours_enforced()
    {
        var harness = Harness.Create();
        harness.Policy.MinRequestHours = 100m;
        var result = await harness.EvaluateDefaultAsync();

        result.IsSuccess.Should().BeFalse();
        result.Error.Should().Be(LeaveRequestMessages.BelowMinimumHours(100m));
    }

    [Fact]
    public async Task Max_request_hours_enforced()
    {
        var harness = Harness.Create();
        harness.Policy.MaxRequestHours = 1m;
        var result = await harness.EvaluateDefaultAsync();

        result.IsSuccess.Should().BeFalse();
        result.Error.Should().Be(LeaveRequestMessages.AboveMaximumHours(1m));
    }

    [Fact]
    public async Task Evaluate_WhenDocumentRequiredAndMissing_Fails()
    {
        var harness = Harness.Create();
        harness.LeaveType.RequiresDocument = true;
        harness.LeaveType.DocumentRequiredAfterHours = null;
        var result = await harness.EvaluateDefaultAsync();

        result.IsSuccess.Should().BeFalse();
        result.Error.Should().Contain("supporting document");
    }

    [Fact]
    public async Task Evaluate_WhenRequestIsWithinDocumentFreeHours_Succeeds()
    {
        var harness = Harness.Create();
        harness.LeaveType.RequiresDocument = true;
        harness.LeaveType.DocumentRequiredAfterHours = 100m;
        var result = await harness.EvaluateDefaultAsync();

        result.IsSuccess.Should().BeTrue();
    }

    [Fact]
    public async Task Evaluate_WhenRequestExceedsDocumentFreeHours_Fails()
    {
        var harness = Harness.Create();
        harness.LeaveType.RequiresDocument = true;
        harness.LeaveType.DocumentRequiredAfterHours = 1m;
        var result = await harness.EvaluateDefaultAsync();

        result.IsSuccess.Should().BeFalse();
        result.Error.Should().Contain("more than 1 hours");
    }

    [Fact]
    public async Task Evaluate_CrossYear_Fails()
    {
        var harness = Harness.Create();
        var result = await harness.Sut.EvaluateAsync(
            harness.TenantId, harness.UserId, null, harness.LeaveTypeId,
            At(2026, 12, 28, 9), At(2027, 1, 5, 18), null, [], CancellationToken.None);

        result.Error.Should().Be(LeaveRequestMessages.CrossYear);
    }

    [Fact]
    public async Task Evaluate_WhenWorkWindowUnset_Returns400WorkWindowRequired()
    {
        var harness = Harness.Create(workWindowSet: false);
        var result = await harness.EvaluateDefaultAsync();

        result.IsSuccess.Should().BeFalse();
        result.StatusCode.Should().Be(400);
        result.Error.Should().Be(LeaveRequestMessages.WorkWindowRequired);
    }

    [Fact]
    public async Task Evaluate_AfternoonPartial_ReturnsFourHours()
    {
        var harness = Harness.Create();
        var result = await harness.Sut.EvaluateAsync(
            harness.TenantId, harness.UserId, null, harness.LeaveTypeId,
            At(2026, 8, 18, 14), At(2026, 8, 18, 18), null, [], CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value!.TotalHours.Should().Be(4.00m);
        result.Value.PaidHours.Should().Be(4.00m);
        result.Value.HoursByDate.Should().Equal(4.00m);
    }

    private static DateTimeOffset At(int year, int month, int day, int hour) =>
        new(year, month, day, hour, 0, 0, TimeSpan.Zero);

    internal sealed class Harness
    {
        public Guid TenantId { get; } = Guid.NewGuid();
        public Guid UserId { get; } = Guid.NewGuid();
        public Guid LeaveTypeId { get; } = Guid.NewGuid();
        public Guid LegalEntityId { get; } = Guid.NewGuid();
        public Employee Employee { get; }
        public LeaveType LeaveType { get; }
        public LeavePolicyVersion Policy { get; private set; } = null!;
        public Mock<ILeavePolicyResolver> Resolver { get; } = new();
        public bool PolicyResolved { get; set; } = true;
        public Mock<IDateTimeProvider> Clock { get; } = new();
        public Mock<ILeaveRequestRepository> Requests { get; } = new();
        public Mock<ILeaveApproverResolver> Approvers { get; } = new();
        public Mock<IEmployeeRepository> Employees { get; } = new();
        public Mock<ILeaveEntitlementRepository> Entitlements { get; } = new();
        public LeaveRequestSubmissionEvaluator Sut { get; }
        public LeaveEntitlement Entitlement { get; }

        private Harness(decimal remainingHours, bool allowUnpaid, bool allowBackdated, bool workWindowSet)
        {
            Employee = new Employee
            {
                Id = Guid.NewGuid(),
                TenantId = TenantId,
                UserId = UserId,
                FirstName = "Priya",
                LastName = "Nair",
                EmployeeNumber = "E1",
                Email = "p@test.dev",
                LegalEntityId = LegalEntityId,
                HireDate = new DateOnly(2024, 1, 1)
            };
            LeaveType = new LeaveType
            {
                Id = LeaveTypeId,
                TenantId = TenantId,
                Name = "Annual Leave",
                Code = "AL",
                IsPaid = true,
                IsActive = true,
                ApplicableGender = LeaveGenderRestrictions.All
            };

            var entitlement = new LeaveEntitlement
            {
                Id = Guid.NewGuid(),
                TenantId = TenantId,
                EmployeeId = Employee.Id,
                LeaveTypeId = LeaveTypeId,
                Year = 2026,
                TotalHours = remainingHours,
                UsedHours = 0m,
                PendingHours = 0m,
                CarriedForwardHours = 0m,
                Source = LeaveEntitlementSources.Auto
            };
            Entitlement = entitlement;

            var resolved = LeaveTestData.CreateResolvedPolicy(TenantId, LeaveTypeId, LegalEntityId);
            Policy = resolved.Version;

            Clock.SetupGet(x => x.Today).Returns(new DateOnly(2026, 8, 17));
            Clock.SetupGet(x => x.UtcNow).Returns(new DateTimeOffset(2026, 8, 17, 9, 0, 0, TimeSpan.Zero));

            var employees = Employees;
            employees.Setup(x => x.GetByUserIdAsync(TenantId, UserId, It.IsAny<CancellationToken>())).ReturnsAsync(Employee);
            employees.Setup(x => x.GetByIdAsync(TenantId, Employee.Id, It.IsAny<CancellationToken>())).ReturnsAsync(Employee);

            var leaveTypes = new Mock<ILeaveTypeRepository>();
            leaveTypes.Setup(x => x.GetByIdAsync(TenantId, LeaveTypeId, It.IsAny<CancellationToken>())).ReturnsAsync(LeaveType);

            Entitlements.Setup(x => x.GetTrackedByEmployeeTypeYearAsync(TenantId, Employee.Id, LeaveTypeId, 2026, It.IsAny<CancellationToken>()))
                .ReturnsAsync(entitlement);

            var legalEntity = LeaveTestData.CreateLegalEntity(
                TenantId, LegalEntityId, workWindowSet);
            var policies = new Mock<ILeavePolicyRepository>();
            Resolver.Setup(x => x.ResolveAsync(TenantId, Employee.Id, LeaveTypeId, It.IsAny<DateOnly>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(() => PolicyResolved ? resolved : null);
            policies.Setup(x => x.ListActiveLegalEntitiesByIdsAsync(TenantId, It.IsAny<IReadOnlyCollection<Guid>>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync([legalEntity]);

            Requests.Setup(x => x.HasOverlappingPendingOrApprovedRequestAsync(TenantId, Employee.Id, It.IsAny<DateTimeOffset>(), It.IsAny<DateTimeOffset>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(false);
            Requests.Setup(x => x.AreAvailableFileRecordsAsync(TenantId, It.IsAny<IReadOnlyCollection<Guid>>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(true);

            Approvers.Setup(x => x.ResolveAsync(TenantId, Employee.Id, It.IsAny<DateOnly>(), It.IsAny<DateOnly>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(new LeaveApproverResolution([new LeaveApproverResolutionRow(Guid.NewGuid(), 1, null)]));

            var holidays = new Mock<ILeaveCalendarHolidayProvider>();
            holidays.Setup(x => x.ListHolidaysAsync(
                    TenantId,
                    It.Is<IReadOnlyCollection<Guid>>(ids => ids.Contains(LegalEntityId)),
                    It.IsAny<DateOnly>(),
                    It.IsAny<DateOnly>(),
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync([]);

            var conflicts = new Mock<ILeaveRequestConflictProvider>();
            conflicts.Setup(x => x.ListConflictsAsync(TenantId, Employee.Id, It.IsAny<DateOnly>(), It.IsAny<DateOnly>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync([]);

            var team = new Mock<ILeaveTeamAbsenceWarningService>();
            team.Setup(x => x.BuildWarningAsync(TenantId, Employee.Id, It.IsAny<DateOnly>(), It.IsAny<DateOnly>(), It.IsAny<decimal?>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync((LeaveTeamAbsenceWarning?)null);

            Sut = new LeaveRequestSubmissionEvaluator(
                employees.Object,
                leaveTypes.Object,
                Entitlements.Object,
                policies.Object,
                Resolver.Object,
                Requests.Object,
                new LeaveRequestHourCalculator(),
                holidays.Object,
                Approvers.Object,
                conflicts.Object,
                team.Object,
                Clock.Object,
                Options.Create(new LeaveRequestOptions
                {
                    AllowBackdatedRequests = allowBackdated,
                    MaximumRequestRangeDays = 3660
                }));
        }

        public static Harness Create(
            decimal remainingHours = 160m,
            bool allowUnpaid = false,
            bool allowBackdated = true,
            bool workWindowSet = true) =>
            new(remainingHours, allowUnpaid, allowBackdated, workWindowSet);

        public Task<ONEVO.Application.Common.Models.Result<LeaveRequestEvaluation>> EvaluateDefaultAsync(
            DateTimeOffset? end = null) =>
            Sut.EvaluateAsync(
                TenantId, UserId, null, LeaveTypeId,
                At(2026, 8, 18, 9), end ?? At(2026, 8, 18, 18), null, [], CancellationToken.None);
    }
}
