using FluentAssertions;
using Microsoft.Extensions.Options;
using Moq;
using ONEVO.Application.Common.RepositoryInterfaces;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.Leave.Entitlement.Helpers;
using ONEVO.Application.Features.Leave.Entitlement.RepositoryInterfaces;
using ONEVO.Application.Features.Leave.Policy.RepositoryInterfaces;
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
    public async Task Evaluate_WhenBalanceShortAndUnpaidSplitDisabled_Fails()
    {
        var harness = Harness.Create(remainingHours: 8m, allowUnpaid: false);
        var result = await harness.EvaluateDefaultAsync(end: At(2026, 8, 20, 18));

        result.IsSuccess.Should().BeFalse();
        result.Error.Should().Contain("Insufficient balance");
    }

    [Fact]
    public async Task Evaluate_WhenBalanceShortAndUnpaidSplitEnabled_SplitsPaidAndUnpaid()
    {
        var harness = Harness.Create(remainingHours: 8m, allowUnpaid: true);
        var result = await harness.EvaluateDefaultAsync(end: At(2026, 8, 20, 18));

        result.IsSuccess.Should().BeTrue();
        result.Value!.PaidHours.Should().Be(8m);
        result.Value.UnpaidHours.Should().Be(16m);
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
        harness.LeaveType.MinimumNoticeDays = 10;
        var result = await harness.EvaluateDefaultAsync();

        result.IsSuccess.Should().BeTrue();
        result.Value!.NoticePeriodMissed.Should().BeTrue();
        result.Value.Warnings.Should().Contain(w => w.Code == "notice_period_missed");
    }

    [Fact]
    public async Task Evaluate_WhenDocumentRequiredAndMissing_Fails()
    {
        var harness = Harness.Create();
        harness.LeaveType.RequiresDocument = true;
        harness.LeaveType.DocumentRequiredAfterDays = 0;
        var result = await harness.EvaluateDefaultAsync();

        result.IsSuccess.Should().BeFalse();
        result.Error.Should().Contain("supporting document");
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

    private sealed class Harness
    {
        public Guid TenantId { get; } = Guid.NewGuid();
        public Guid UserId { get; } = Guid.NewGuid();
        public Guid LeaveTypeId { get; } = Guid.NewGuid();
        public Guid LegalEntityId { get; } = Guid.NewGuid();
        public Employee Employee { get; }
        public LeaveType LeaveType { get; }
        public Mock<IDateTimeProvider> Clock { get; } = new();
        public Mock<ILeaveRequestRepository> Requests { get; } = new();
        public Mock<ILeaveApproverResolver> Approvers { get; } = new();
        public LeaveRequestSubmissionEvaluator Sut { get; }

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
                RequiresApproval = true,
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

            var template = PreviewGenerateEntitlementsQueryHandlerTests.CreatePolicyAggregate(
                TenantId, LegalEntityId, 20m, 5m);
            var policy = new LeavePolicyAggregate(
                template.Policy,
                [new LeavePolicyLeaveTypeWithType(
                    new LeavePolicyLeaveType
                    {
                        Id = Guid.NewGuid(),
                        TenantId = TenantId,
                        LeavePolicyId = template.Policy.Id,
                        LeaveTypeId = LeaveTypeId,
                        AnnualEntitlementDays = 20m
                    },
                    "Annual Leave",
                    "AL")],
                template.BlackoutPeriods,
                template.LegalEntities);

            Clock.SetupGet(x => x.Today).Returns(new DateOnly(2026, 8, 17));
            Clock.SetupGet(x => x.UtcNow).Returns(new DateTimeOffset(2026, 8, 17, 9, 0, 0, TimeSpan.Zero));

            var employees = new Mock<IEmployeeRepository>();
            employees.Setup(x => x.GetByUserIdAsync(TenantId, UserId, It.IsAny<CancellationToken>())).ReturnsAsync(Employee);
            employees.Setup(x => x.GetByIdAsync(TenantId, Employee.Id, It.IsAny<CancellationToken>())).ReturnsAsync(Employee);

            var leaveTypes = new Mock<ILeaveTypeRepository>();
            leaveTypes.Setup(x => x.GetByIdAsync(TenantId, LeaveTypeId, It.IsAny<CancellationToken>())).ReturnsAsync(LeaveType);

            var entitlements = new Mock<ILeaveEntitlementRepository>();
            entitlements.Setup(x => x.GetTrackedByEmployeeTypeYearAsync(TenantId, Employee.Id, LeaveTypeId, 2026, It.IsAny<CancellationToken>()))
                .ReturnsAsync(entitlement);

            var legalEntity = PreviewGenerateEntitlementsQueryHandlerTests.CreateLegalEntity(
                TenantId, LegalEntityId, workWindowSet);
            var policies = new Mock<ILeavePolicyRepository>();
            policies.Setup(x => x.ListActiveAggregatesByLegalEntityIdsAsync(TenantId, It.IsAny<IReadOnlyCollection<Guid>>(), 2026, It.IsAny<CancellationToken>()))
                .ReturnsAsync(new Dictionary<Guid, LeavePolicyAggregate> { [LegalEntityId] = policy });
            policies.Setup(x => x.ListActiveLegalEntitiesByIdsAsync(TenantId, It.IsAny<IReadOnlyCollection<Guid>>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync([legalEntity]);

            Requests.Setup(x => x.HasOverlappingPendingOrApprovedRequestAsync(TenantId, Employee.Id, It.IsAny<DateTimeOffset>(), It.IsAny<DateTimeOffset>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(false);
            Requests.Setup(x => x.AreAvailableFileRecordsAsync(TenantId, It.IsAny<IReadOnlyCollection<Guid>>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(true);

            Approvers.Setup(x => x.ResolveAsync(TenantId, Employee.Id, It.IsAny<DateOnly>(), It.IsAny<DateOnly>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(new LeaveApproverResolution([new LeaveApproverResolutionRow(Guid.NewGuid(), 1, null)]));

            var holidays = new Mock<ILeaveHolidayProvider>();
            holidays.Setup(x => x.ListHolidaysAsync(TenantId, LegalEntityId, It.IsAny<DateOnly>(), It.IsAny<DateOnly>(), It.IsAny<CancellationToken>()))
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
                entitlements.Object,
                policies.Object,
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
                    AllowUnpaidSplitWhenBalanceShort = allowUnpaid,
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
