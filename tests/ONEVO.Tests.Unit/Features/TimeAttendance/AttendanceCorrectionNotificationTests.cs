using FluentAssertions;
using Moq;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Common.RepositoryInterfaces;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.CoreHr.EmployeeAuthority.Models;
using ONEVO.Application.Features.CoreHr.EmployeeAuthority.ServiceInterfaces;
using ONEVO.Application.Features.OrgStructure.RepositoryInterfaces;
using ONEVO.Application.Features.TimeAttendance.Commands.AttendanceCorrections;
using ONEVO.Application.Features.TimeAttendance.DTOs.Responses;
using ONEVO.Application.Features.TimeAttendance.Queries.AttendanceCorrections;
using ONEVO.Application.Features.TimeAttendance.RepositoryInterfaces;
using ONEVO.Domain.Features.CoreHr.Entities;
using ONEVO.Domain.Features.OrgStructure.Entities;
using ONEVO.Domain.Features.TimeAttendance.Entities;
using ONEVO.Tests.Unit.Fakes;
using CoreEmployeeRepository = ONEVO.Application.Features.CoreHr.Employee.RepositoryInterfaces.IEmployeeRepository;

namespace ONEVO.Tests.Unit.Features.TimeAttendance;

public sealed class AttendanceCorrectionNotificationTests
{
    [Fact]
    public async Task Request_WhenApprovalRequired_CreatesApproverNotificationInsideTransaction()
    {
        var fixture = new Fixture(approvalRequired: true);

        var result = await fixture.Workflow.RequestAsync(fixture.Request(), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value!.Status.Should().Be(AttendanceCorrection.StatusPending);
        result.Value.ApprovalRequired.Should().BeTrue();
        fixture.AddedCorrection!.ApprovalRequired.Should().BeTrue();
        fixture.UnitOfWork.TransactionCallCount.Should().Be(1);
        fixture.UnitOfWork.IsInTransaction.Should().BeFalse();
        fixture.NotificationWasCreatedInsideTransaction.Should().BeTrue();
        fixture.Notifications.Should().ContainSingle(x =>
            x.RecipientUserId == fixture.ApproverUserId
            && x.TemplateCode == "attendance_correction_request_created"
            && x.RelatedEntityType == "attendance_correction"
            && x.RelatedEntityId == result.Value.Id);
    }

    [Fact]
    public async Task Request_ResponseIncludesLegalEntityTimezone()
    {
        var fixture = new Fixture(approvalRequired: true, timezone: "Asia/Colombo");

        var result = await fixture.Workflow.RequestAsync(fixture.Request(), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value!.Timezone.Should().Be("Asia/Colombo");
    }

    [Fact]
    public async Task ApprovalInboxRowsIncludeLegalEntityTimezoneWithoutTenantId()
    {
        var fixture = new Fixture(approvalRequired: true, actingAsApprover: true, timezone: "Asia/Colombo");
        var pending = fixture.PendingCorrection();
        fixture.Corrections.Setup(x => x.ListApprovalInboxAsync(
                fixture.TenantId, fixture.LegalEntityId, It.IsAny<IReadOnlyCollection<Guid>>(),
                It.IsAny<DateOnly?>(), It.IsAny<DateOnly?>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new[] { pending });
        fixture.Attendance.Setup(x => x.ListEmployeeIdentitiesAsync(
                fixture.TenantId, fixture.LegalEntityId, It.IsAny<IReadOnlyCollection<Guid>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Dictionary<Guid, AttendanceHistoryEmployee>
            {
                [fixture.EmployeeId] = new(fixture.EmployeeId, "Alex Employee", "E-1", null, null, null)
            });

        var result = await fixture.Workflow.ListApprovalsAsync(
            new ListAttendanceCorrectionApprovalsQuery(null, null, "pending"), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().ContainSingle();
        result.Value![0].Timezone.Should().Be("Asia/Colombo");
        typeof(AttendanceCorrectionResponse).GetProperty("TenantId").Should().BeNull();
    }

    [Theory]
    [InlineData(null)]
    [InlineData("Invalid/Timezone")]
    public async Task Reject_WhenLegalEntityTimezoneIsMissingOrInvalid_FallsBackToUtc(string? timezone)
    {
        var fixture = new Fixture(approvalRequired: true, actingAsApprover: true, timezone: timezone);
        var pending = fixture.PendingCorrection();
        fixture.Corrections.Setup(x => x.GetTrackedByIdAsync(fixture.TenantId, pending.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(pending);

        var result = await fixture.Workflow.RejectAsync(
            new RejectAttendanceCorrectionCommand(pending.Id, "Please provide more detail."), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value!.Timezone.Should().Be("UTC");
    }

    [Fact]
    public async Task Request_WhenAutoApproved_DoesNotCreateApprovalRequestNotification()
    {
        var fixture = new Fixture(approvalRequired: false);

        var result = await fixture.Workflow.RequestAsync(fixture.Request(), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value!.Status.Should().Be(AttendanceCorrection.StatusApproved);
        result.Value.ApprovalRequired.Should().BeFalse();
        fixture.AddedCorrection!.ApprovalRequired.Should().BeFalse();
        fixture.Notifications.Should().NotContain(x =>
            x.TemplateCode == "attendance_correction_request_created");
        fixture.Authority.Verify(x => x.ResolveApproverAsync(
            It.IsAny<EmployeeApprovalRouteRequest>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Approve_NotifiesRequesterAndAppliesCorrectionInSameTransaction()
    {
        var fixture = new Fixture(approvalRequired: true, actingAsApprover: true);
        var pending = fixture.PendingCorrection();
        fixture.Corrections.Setup(x => x.GetTrackedByIdAsync(fixture.TenantId, pending.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(pending);
        fixture.Attendance.Setup(x => x.GetTrackedRecordAsync(
                fixture.TenantId, fixture.EmployeeId, fixture.WorkDate, It.IsAny<CancellationToken>()))
            .ReturnsAsync(fixture.Record);

        var result = await fixture.Workflow.ApproveAsync(
            new ApproveAttendanceCorrectionCommand(pending.Id, "Approved."), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value!.Status.Should().Be(AttendanceCorrection.StatusApproved);
        result.Value.ApprovalRequired.Should().BeTrue();
        fixture.Record.ActualStart.Should().Be(pending.RequestedClockInAt);
        fixture.Notifications.Should().ContainSingle(x =>
            x.RecipientUserId == fixture.RequesterUserId
            && x.TemplateCode == "attendance_correction_request_decided"
            && x.RelatedEntityId == pending.Id);
        fixture.UnitOfWork.TransactionCallCount.Should().Be(1);
    }

    [Fact]
    public async Task Reject_NotifiesRequesterWithoutMutatingAttendanceRecord()
    {
        var fixture = new Fixture(approvalRequired: true, actingAsApprover: true);
        var pending = fixture.PendingCorrection();
        var originalStart = fixture.Record.ActualStart;
        fixture.Corrections.Setup(x => x.GetTrackedByIdAsync(fixture.TenantId, pending.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(pending);

        var result = await fixture.Workflow.RejectAsync(
            new RejectAttendanceCorrectionCommand(pending.Id, "Please provide more detail."), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value!.Status.Should().Be(AttendanceCorrection.StatusRejected);
        result.Value.ApprovalRequired.Should().BeTrue();
        fixture.Record.ActualStart.Should().Be(originalStart);
        fixture.Notifications.Should().ContainSingle(x =>
            x.RecipientUserId == fixture.RequesterUserId
            && x.TemplateCode == "attendance_correction_request_decided");
    }

    [Fact]
    public async Task Cancel_PreservesApprovalRequiredSnapshot()
    {
        var fixture = new Fixture(approvalRequired: true);
        var pending = fixture.PendingCorrection();
        fixture.Corrections.Setup(x => x.GetTrackedByIdAsync(fixture.TenantId, pending.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(pending);

        var result = await fixture.Workflow.CancelAsync(
            new CancelAttendanceCorrectionCommand(pending.Id), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value!.Status.Should().Be(AttendanceCorrection.StatusCancelled);
        result.Value.ApprovalRequired.Should().BeTrue();
    }

    [Fact]
    public async Task ListMy_DoesNotRecalculateApprovalRequirementFromCurrentPolicy()
    {
        var fixture = new Fixture(approvalRequired: true);
        var correction = fixture.PendingCorrection();
        correction.Status = AttendanceCorrection.StatusApproved;
        correction.ApprovalRequired = true;
        fixture.Policies.Setup(x => x.ListByLegalEntityAsync(fixture.TenantId, fixture.LegalEntityId, false, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new[] { fixture.CreatePolicy(approvalRequired: false) });
        fixture.Corrections.Setup(x => x.ListMyAsync(fixture.TenantId, fixture.EmployeeId,
                It.IsAny<DateOnly?>(), It.IsAny<DateOnly?>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new[] { correction });

        var result = await fixture.Workflow.ListMyAsync(
            new ListMyAttendanceCorrectionsQuery(null, null, null), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().ContainSingle();
        result.Value![0].ApprovalRequired.Should().BeTrue();
    }

    [Fact]
    public async Task Request_WhenValidationFails_DoesNotCreateCorrectionOrNotification()
    {
        var fixture = new Fixture(approvalRequired: true);

        var result = await fixture.Workflow.RequestAsync(
            fixture.Request() with { Reason = " " }, CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.StatusCode.Should().Be(400);
        fixture.Corrections.Verify(x => x.AddAsync(It.IsAny<AttendanceCorrection>(), It.IsAny<CancellationToken>()), Times.Never);
        fixture.Notifications.Should().BeEmpty();
        fixture.Corrections.Verify(x => x.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Request_WhenApproverResolutionFails_DoesNotCreateCorrectionOrNotification()
    {
        var fixture = new Fixture(approvalRequired: true);
        fixture.Authority.Setup(x => x.ResolveApproverAsync(
                It.IsAny<EmployeeApprovalRouteRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<EmployeeApprovalRoute>.UnprocessableEntity("No eligible approver."));

        var result = await fixture.Workflow.RequestAsync(fixture.Request(), CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.StatusCode.Should().Be(409);
        fixture.Corrections.Verify(x => x.AddAsync(It.IsAny<AttendanceCorrection>(), It.IsAny<CancellationToken>()), Times.Never);
        fixture.Notifications.Should().BeEmpty();
        fixture.Corrections.Verify(x => x.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    private sealed class Fixture
    {
        public readonly Guid TenantId = Guid.NewGuid();
        public readonly Guid RequesterUserId = Guid.NewGuid();
        public readonly Guid ApproverUserId = Guid.NewGuid();
        public readonly Guid EmployeeId = Guid.NewGuid();
        public readonly Guid ApproverEmployeeId = Guid.NewGuid();
        public readonly Guid LegalEntityId = Guid.NewGuid();
        public readonly Guid PositionId = Guid.NewGuid();
        public readonly DateOnly WorkDate = new(2026, 8, 24);
        public readonly DateTimeOffset Now = new(2026, 8, 24, 10, 0, 0, TimeSpan.Zero);

        public readonly Mock<ICurrentUser> CurrentUser = new();
        public readonly Mock<IDateTimeProvider> Clock = new();
        public readonly Mock<CoreEmployeeRepository> Employees = new();
        public readonly Mock<ILegalEntityRepository> LegalEntities = new();
        public readonly Mock<IClockInPolicyRepository> Policies = new();
        public readonly Mock<IAttendanceReadRepository> Attendance = new();
        public readonly Mock<IAttendanceCorrectionRepository> Corrections = new();
        public readonly Mock<IEmployeeAuthorityResolver> Authority = new();
        public readonly Mock<IPositionRepository> Positions = new();
        public readonly Mock<INotificationDispatcher> Dispatcher = new();
        public readonly FakeUnitOfWork UnitOfWork = new();
        public readonly List<NotificationCall> Notifications = [];
        public bool NotificationWasCreatedInsideTransaction { get; private set; }
        public readonly AttendanceRecord Record;
        public AttendanceCorrection? AddedCorrection { get; private set; }
        public AttendanceCorrectionWorkflow Workflow;

        public Fixture(bool approvalRequired, bool actingAsApprover = false, string? timezone = "UTC")
        {
            var actorUserId = actingAsApprover ? ApproverUserId : RequesterUserId;
            CurrentUser.SetupGet(x => x.IsAuthenticated).Returns(true);
            CurrentUser.SetupGet(x => x.UserId).Returns(actorUserId);
            CurrentUser.SetupGet(x => x.TenantId).Returns(TenantId);
            CurrentUser.Setup(x => x.HasPermission(It.IsAny<string>())).Returns(true);
            Clock.SetupGet(x => x.UtcNow).Returns(Now);
            Clock.SetupGet(x => x.Today).Returns(WorkDate);

            var employee = new Employee
            {
                Id = EmployeeId, TenantId = TenantId, UserId = RequesterUserId,
                LegalEntityId = LegalEntityId, FirstName = "Alex", LastName = "Employee"
            };
            var approverEmployee = new Employee
            {
                Id = ApproverEmployeeId, TenantId = TenantId, UserId = ApproverUserId,
                LegalEntityId = LegalEntityId, FirstName = "Sarah", LastName = "Approver"
            };
            var legalEntity = new LegalEntity
            {
                Id = LegalEntityId, TenantId = TenantId, Name = "Acme",
                Timezone = timezone, WorkStartTime = new TimeOnly(9), WorkEndTime = new TimeOnly(17),
                StandardWorkingDays = "[1,2,3,4,5]", BreakDurationMinutes = 60
            };
            Policy = new ClockInPolicy
            {
                Id = Guid.NewGuid(), TenantId = TenantId, LegalEntityId = LegalEntityId,
                ScopeType = ClockInPolicy.ScopeFullCompany, EffectiveFrom = WorkDate.AddDays(-10),
                CorrectionRequiresApproval = approvalRequired
            };
            Record = new AttendanceRecord
            {
                Id = Guid.NewGuid(), TenantId = TenantId, EmployeeId = EmployeeId, Date = WorkDate,
                ExpectedWorkingDay = true, ScheduledStart = new TimeOnly(9), ScheduledEnd = new TimeOnly(17),
                Status = AttendanceRecord.StatusNotClockedIn, CreatedAt = Now, UpdatedAt = Now
            };

            Employees.Setup(x => x.GetDefaultForUserAsync(TenantId, It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(employee);
            Employees.Setup(x => x.GetByIdAsync(TenantId, EmployeeId, It.IsAny<CancellationToken>()))
                .ReturnsAsync(employee);
            Employees.Setup(x => x.GetByIdAsync(TenantId, ApproverEmployeeId, It.IsAny<CancellationToken>()))
                .ReturnsAsync(approverEmployee);
            LegalEntities.Setup(x => x.GetByIdForTenantAsync(TenantId, LegalEntityId, It.IsAny<CancellationToken>()))
                .ReturnsAsync(legalEntity);
            Policies.Setup(x => x.ListByLegalEntityAsync(TenantId, LegalEntityId, false, It.IsAny<CancellationToken>()))
                .ReturnsAsync(new[] { Policy });
            Attendance.Setup(x => x.GetTrackedRecordAsync(TenantId, EmployeeId, WorkDate, It.IsAny<CancellationToken>()))
                .ReturnsAsync(Record);
            Attendance.Setup(x => x.ListBreaksAsync(TenantId, EmployeeId, It.IsAny<DateTimeOffset>(), It.IsAny<DateTimeOffset>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(Array.Empty<BreakRecord>());
            Attendance.Setup(x => x.AddRecordAsync(It.IsAny<AttendanceRecord>(), It.IsAny<CancellationToken>()))
                .Returns(Task.CompletedTask);
            Attendance.Setup(x => x.AddBreakAsync(It.IsAny<BreakRecord>(), It.IsAny<CancellationToken>()))
                .Returns(Task.CompletedTask);
            Attendance.Setup(x => x.DeleteBreakAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
                .Returns(Task.CompletedTask);
            Corrections.Setup(x => x.HasPendingForRecordAsync(TenantId, EmployeeId, It.IsAny<Guid?>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(false);
            Corrections.Setup(x => x.AddAsync(It.IsAny<AttendanceCorrection>(), It.IsAny<CancellationToken>()))
                .Callback<AttendanceCorrection, CancellationToken>((correction, _) => AddedCorrection = correction)
                .Returns(Task.CompletedTask);
            Corrections.Setup(x => x.SaveChangesAsync(It.IsAny<CancellationToken>())).ReturnsAsync(1);
            Positions.Setup(x => x.GetByIdAsync(TenantId, PositionId, It.IsAny<CancellationToken>()))
                .ReturnsAsync(new Position { Id = PositionId, TenantId = TenantId, LegalEntityId = LegalEntityId, Name = "HR Manager" });
            Authority.Setup(x => x.ResolveVisibilityAsync(
                    It.IsAny<EmployeeAuthorityVisibilityRequest>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(new EmployeeAuthorityVisibilityScope(
                    actorUserId, LegalEntityId, false, new[] { EmployeeId }));
            Authority.Setup(x => x.ResolveApproverAsync(
                    It.IsAny<EmployeeApprovalRouteRequest>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(Result<EmployeeApprovalRoute>.Success(new EmployeeApprovalRoute(
                    ApproverEmployeeId, ApproverUserId, PositionId, "attendance:approve",
                    EmployeeAuthorityPurpose.AttendanceCorrectionApproval,
                    EmployeeApprovalRouteSource.PositionCoverage, 1)));
            Dispatcher.Setup(x => x.SendTemplatedAsync(
                    It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<string>(),
                    It.IsAny<IReadOnlyDictionary<string, string>>(), It.IsAny<string?>(), It.IsAny<Guid?>(),
                    It.IsAny<CancellationToken>()))
                .Callback((Guid tenantId, Guid userId, string code, IReadOnlyDictionary<string, string> placeholders,
                    string? relatedType, Guid? relatedId, CancellationToken _) =>
                    {
                        NotificationWasCreatedInsideTransaction |= UnitOfWork.IsInTransaction;
                        Notifications.Add(new NotificationCall(tenantId, userId, code, relatedType, relatedId));
                    })
                .Returns(Task.CompletedTask);

            Workflow = new AttendanceCorrectionWorkflow(CurrentUser.Object, Clock.Object, Employees.Object,
                LegalEntities.Object, Policies.Object, Attendance.Object, Corrections.Object,
                Authority.Object, Positions.Object, Dispatcher.Object, UnitOfWork);
        }

        public ClockInPolicy Policy { get; }

        public ClockInPolicy CreatePolicy(bool approvalRequired) => new()
        {
            Id = Guid.NewGuid(), TenantId = TenantId, LegalEntityId = LegalEntityId,
            ScopeType = ClockInPolicy.ScopeFullCompany, EffectiveFrom = WorkDate.AddDays(-10),
            CorrectionRequiresApproval = approvalRequired
        };

        public RequestAttendanceCorrectionCommand Request() => new(
            WorkDate, AttendanceCorrection.TypeClockIn,
            new DateTimeOffset(2026, 8, 24, 9, 15, 0, TimeSpan.Zero), null, null,
            "Forgot to clock in.", "Test notes");

        public AttendanceCorrection PendingCorrection() => new()
        {
            Id = Guid.NewGuid(), TenantId = TenantId, EmployeeId = EmployeeId,
            LegalEntityId = LegalEntityId, AttendanceRecordId = Record.Id, WorkDate = WorkDate,
            CorrectionType = AttendanceCorrection.TypeClockIn,
            RequestedClockInAt = new DateTimeOffset(2026, 8, 24, 9, 15, 0, TimeSpan.Zero),
            OriginalBreakJson = "[]", RequestedById = RequesterUserId,
            Status = AttendanceCorrection.StatusPending, ApprovalRequired = true, Reason = "Forgot to clock in.",
            CreatedAt = Now, UpdatedAt = Now
        };
    }

    private sealed record NotificationCall(
        Guid TenantId, Guid RecipientUserId, string TemplateCode,
        string? RelatedEntityType, Guid? RelatedEntityId);
}
