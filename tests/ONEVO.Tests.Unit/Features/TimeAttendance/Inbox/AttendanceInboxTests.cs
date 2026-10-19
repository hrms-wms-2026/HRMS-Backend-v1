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
using ONEVO.Application.Features.Monitoring.TrayActivation.RepositoryInterfaces;
using ONEVO.Application.Features.OrgStructure.RepositoryInterfaces;
using ONEVO.Application.Features.TimeAttendance.Commands.AttendanceCorrections;
using ONEVO.Application.Features.TimeAttendance.DTOs.Responses;
using ONEVO.Application.Features.TimeAttendance.Inbox;
using ONEVO.Application.Features.TimeAttendance.RepositoryInterfaces;
using ONEVO.Domain.Features.CoreHr.Entities;
using ONEVO.Domain.Features.Inbox.Entities;
using ONEVO.Domain.Features.Monitoring.TrayActivation.Entities;
using ONEVO.Domain.Features.TimeAttendance.Entities;
using ONEVO.Tests.Unit.Fakes;
using ONEVO.Tests.Unit.Features.Inbox;
using Xunit;

namespace ONEVO.Tests.Unit.Features.TimeAttendance.Inbox;

/// <summary>Attendance requests in the Inbox (plan A1): scope = act, D-c device scoping, journaled snapshots, revert.</summary>
public class AttendanceInboxTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 14, 10, 0, 0, TimeSpan.Zero);

    private sealed class Fixture
    {
        public Guid TenantId { get; } = Guid.NewGuid();
        public Guid LegalEntityId { get; } = Guid.NewGuid();
        public Guid ApproverUserId { get; } = Guid.NewGuid();
        public Employee Approver { get; }
        public Employee Worker { get; }
        public Mock<ICurrentUser> CurrentUser { get; } = new();
        public Mock<IEmployeeRepository> Employees { get; } = new();
        public Mock<IEmployeeAuthorityResolver> Authority { get; } = new();
        public Mock<IAttendanceCorrectionRepository> Corrections { get; } = new();
        public Mock<IWorkAreaChangeRequestRepository> WorkAreas { get; } = new();
        public Mock<ILocationChangeRequestRepository> Locations { get; } = new();
        public Mock<IDeviceChangeRequestRepository> Devices { get; } = new();
        public Mock<IAttendanceReadRepository> Attendance { get; } = new();
        public Mock<IEmployeeWorkLocationRepository> WorkLocations { get; } = new();
        public Mock<ITrayActivationRepository> Tray { get; } = new();
        public Mock<ISender> Sender { get; } = new();
        public InMemoryInboxDecisionJournal Journal { get; } = new();
        public FakeDateTimeProvider Clock { get; } = new() { UtcNow = Now };
        public HashSet<Guid> Scope { get; } = new();
        public Mock<ONEVO.Application.Features.TimeAttendance.Alerts.IAttendanceAlertRepository> Alerts { get; } = new();
        public Mock<ONEVO.Application.Features.Leave.Entitlement.RepositoryInterfaces.ILeaveEntitlementRepository> Entitlements { get; } = new();
        public Mock<ONEVO.Application.Features.Leave.Approval.RepositoryInterfaces.ILeaveApprovalRepository> LeaveApprovals { get; } = new();
        public List<ONEVO.Domain.Features.TimeAttendance.Alerts.AttendanceAlert> OpenAlerts { get; } = new();
        public List<ONEVO.Domain.Features.TimeAttendance.Alerts.AttendanceAlert> OwnAlerts { get; } = new();

        public Fixture()
        {
            Approver = new Employee { Id = Guid.NewGuid(), TenantId = TenantId, UserId = ApproverUserId, FirstName = "Anu", LegalEntityId = LegalEntityId };
            Worker = new Employee { Id = Guid.NewGuid(), TenantId = TenantId, UserId = Guid.NewGuid(), FirstName = "Bala", LegalEntityId = LegalEntityId };
            CurrentUser.SetupGet(x => x.TenantId).Returns(TenantId);
            CurrentUser.SetupGet(x => x.UserId).Returns(ApproverUserId);
            CurrentUser.Setup(x => x.HasPermission("attendance:approve")).Returns(true);
            foreach (var e in new[] { Approver, Worker })
                Employees.Setup(x => x.GetByIdAsync(TenantId, e.Id, It.IsAny<CancellationToken>())).ReturnsAsync(e);
            Employees.Setup(x => x.GetByUserIdsAsync(TenantId, It.IsAny<IReadOnlyList<Guid>>(), It.IsAny<CancellationToken>())).ReturnsAsync([Approver]);
            Employees.Setup(x => x.ListByIdsAsync(TenantId, It.IsAny<IReadOnlyCollection<Guid>>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(new Dictionary<Guid, Employee> { [Approver.Id] = Approver, [Worker.Id] = Worker });
            Authority.Setup(a => a.ResolveVisibilityAsync(It.IsAny<EmployeeAuthorityVisibilityRequest>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(new EmployeeAuthorityVisibilityScope(ApproverUserId, LegalEntityId, false, [Worker.Id]));
            Authority.Setup(a => a.ResolveApprovalInboxScopeAsync(It.IsAny<EmployeeApprovalInboxScopeRequest>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync((EmployeeApprovalInboxScopeRequest r, CancellationToken _) => r.CandidateEmployeeIds.Where(Scope.Contains).ToList());
            Corrections.Setup(x => x.ListMyAsync(TenantId, It.IsAny<Guid>(), It.IsAny<DateOnly?>(), It.IsAny<DateOnly?>(), It.IsAny<string?>(), It.IsAny<int>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync((Array.Empty<AttendanceCorrection>(), 0));
            Corrections.Setup(x => x.ListApprovalInboxAsync(TenantId, LegalEntityId, It.IsAny<IReadOnlyCollection<Guid>>(), null, null, "pending", It.IsAny<CancellationToken>()))
                .ReturnsAsync([]);
            WorkAreas.Setup(x => x.ListMyAsync(TenantId, It.IsAny<Guid>(), null, null, null, It.IsAny<int>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync((Array.Empty<WorkAreaChangeRequest>(), 0));
            WorkAreas.Setup(x => x.ListPendingEmployeeIdsAsync(TenantId, LegalEntityId, null, null, It.IsAny<CancellationToken>())).ReturnsAsync([]);
            Locations.Setup(x => x.ListMyAsync(TenantId, It.IsAny<Guid>(), null, It.IsAny<int>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync((Array.Empty<LocationChangeRequest>(), 0));
            Locations.Setup(x => x.ListPendingEmployeeIdsAsync(TenantId, LegalEntityId, It.IsAny<CancellationToken>())).ReturnsAsync([]);
            Devices.Setup(x => x.ListPendingEmployeeIdsAsync(TenantId, LegalEntityId, It.IsAny<CancellationToken>())).ReturnsAsync([]);
            Devices.Setup(x => x.ListPendingWithoutLegalEntityAsync(TenantId, It.IsAny<CancellationToken>())).ReturnsAsync([]);
            Devices.Setup(x => x.ListForEmployeeAsync(TenantId, It.IsAny<Guid>(), It.IsAny<CancellationToken>())).ReturnsAsync([]);
            Devices.Setup(x => x.ListApprovalInboxAsync(TenantId, LegalEntityId, It.IsAny<IReadOnlyCollection<Guid>>(), It.IsAny<int>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync((Array.Empty<DeviceChangeRequest>(), 0));
            Alerts.Setup(x => x.ListOpenEmployeeIdsAsync(TenantId, LegalEntityId, It.IsAny<CancellationToken>()))
                .ReturnsAsync(() => OpenAlerts.Select(a => a.EmployeeId).Distinct().ToList());
            Alerts.Setup(x => x.ListOpenAsync(TenantId, LegalEntityId, It.IsAny<IReadOnlyCollection<Guid>>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync((Guid _, Guid _, IReadOnlyCollection<Guid> ids, int take, CancellationToken _) =>
                    OpenAlerts.Where(a => ids.Contains(a.EmployeeId)).OrderByDescending(a => a.WorkDate).ThenBy(a => a.Id).Take(take).ToList());
            Alerts.Setup(x => x.ListForEmployeeAsync(TenantId, It.IsAny<Guid>(), It.IsAny<DateOnly>(), It.IsAny<DateOnly>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(() => OwnAlerts.ToList());
            Alerts.Setup(x => x.GetByIdAsync(TenantId, It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync((Guid _, Guid id, CancellationToken _) => OpenAlerts.Concat(OwnAlerts).FirstOrDefault(a => a.Id == id));
        }

        public Mock<IConversationRepository> Conversations()
        {
            var conversations = new Mock<IConversationRepository>();
            conversations.Setup(x => x.ListAddedSubjectsAsync(TenantId, It.IsAny<Guid>(), "attendance", It.IsAny<CancellationToken>())).ReturnsAsync([]);
            return conversations;
        }

        public AttendanceInboxSourceProvider Provider() => new(
            CurrentUser.Object, Employees.Object, Authority.Object, Corrections.Object, WorkAreas.Object, Locations.Object, Devices.Object,
            Attendance.Object, WorkLocations.Object, Tray.Object, Mock.Of<IWorkModeRepository>(), Mock.Of<ILegalEntityRepository>(),
            Sender.Object, Journal, new FakeUnitOfWork(), Conversations().Object, Clock, AlertInbox());

        public AttendanceAlertInbox AlertInbox() => new(
            CurrentUser.Object, Employees.Object, Authority.Object, Alerts.Object, Entitlements.Object, LeaveApprovals.Object, Sender.Object,
            Journal, new FakeUnitOfWork(), Clock);

        public ONEVO.Domain.Features.TimeAttendance.Alerts.AttendanceAlert Alert(Guid employeeId, string signal = ONEVO.Domain.Features.TimeAttendance.Alerts.AttendanceAlertSignals.AbsentDays) => new()
        {
            Id = Guid.NewGuid(), TenantId = TenantId, EmployeeId = employeeId, LegalEntityId = LegalEntityId, WorkDate = new DateOnly(2026, 10, 9),
            SignalKey = signal, Status = ONEVO.Domain.Features.TimeAttendance.Alerts.AttendanceAlertStatuses.Open, DetectedAt = Now.AddDays(-4),
        };

        public AttendanceCorrection Correction(string status = AttendanceCorrection.StatusPending) => new()
        {
            Id = Guid.NewGuid(), TenantId = TenantId, EmployeeId = Worker.Id, LegalEntityId = LegalEntityId, WorkDate = new DateOnly(2026, 10, 13),
            CorrectionType = AttendanceCorrection.TypeClockIn, RequestedClockInAt = new DateTimeOffset(2026, 10, 13, 9, 0, 0, TimeSpan.Zero),
            Status = status, Reason = "forgot", CreatedAt = Now.AddDays(-1), UpdatedAt = Now.AddDays(-1),
        };
    }

    [Fact]
    public async Task Received_lists_only_requests_the_caller_is_the_exact_approver_of()
    {
        var f = new Fixture();
        var mine = f.Correction();
        var someoneElses = f.Correction();
        someoneElses.EmployeeId = Guid.NewGuid();
        f.Corrections.Setup(x => x.ListApprovalInboxAsync(f.TenantId, f.LegalEntityId, It.IsAny<IReadOnlyCollection<Guid>>(), null, null, "pending", It.IsAny<CancellationToken>()))
            .ReturnsAsync([mine, someoneElses]);
        f.Scope.Add(f.Worker.Id);

        var items = await f.Provider().ListAsync(f.TenantId, new InboxQuery(), f.Approver.Id, default);

        items.Select(i => i.Id).Should().Equal(mine.Id);
        items[0].Direction.Should().Be(InboxDirections.Received);
    }

    [Fact]
    public async Task Without_attendance_approve_nothing_is_received_and_decisions_are_refused()
    {
        var f = new Fixture();
        var correction = f.Correction();
        f.Corrections.Setup(x => x.ListApprovalInboxAsync(f.TenantId, f.LegalEntityId, It.IsAny<IReadOnlyCollection<Guid>>(), null, null, "pending", It.IsAny<CancellationToken>()))
            .ReturnsAsync([correction]);
        f.Scope.Add(f.Worker.Id);
        f.CurrentUser.Setup(x => x.HasPermission("attendance:approve")).Returns(false);

        var items = await f.Provider().ListAsync(f.TenantId, new InboxQuery(), f.Approver.Id, default);
        var approve = await f.Provider().ExecuteActionAsync(f.TenantId, "correction", correction.Id, "approve", null, f.Approver.Id, default);

        items.Should().BeEmpty();
        approve.StatusCode.Should().Be(403);
        f.Sender.Invocations.Should().BeEmpty();
    }

    [Fact]
    public async Task A_device_request_without_its_own_legal_entity_is_scoped_through_the_employees()
    {
        var f = new Fixture();
        var request = new DeviceChangeRequest
        {
            Id = Guid.NewGuid(), TenantId = f.TenantId, EmployeeId = f.Worker.Id, LegalEntityId = null, NewDeviceName = "Laptop", NewDeviceOs = "Windows",
            Status = DeviceChangeRequest.StatusPending, RequestedAt = Now,
        };
        f.Devices.Setup(x => x.ListPendingWithoutLegalEntityAsync(f.TenantId, It.IsAny<CancellationToken>())).ReturnsAsync([request]);
        f.Scope.Add(f.Worker.Id);

        var items = await f.Provider().ListAsync(f.TenantId, new InboxQuery(), f.Approver.Id, default);

        items.Should().ContainSingle(i => i.Id == request.Id && i.Kind == "device_change" && i.ContextId == f.LegalEntityId);
    }

    [Fact]
    public async Task Approving_a_correction_journals_the_attendance_day_as_it_was()
    {
        var f = new Fixture();
        var correction = f.Correction();
        f.Corrections.Setup(x => x.GetByIdAsync(f.TenantId, correction.Id, It.IsAny<CancellationToken>())).ReturnsAsync(correction);
        f.Attendance.Setup(x => x.GetRecordAsync(f.TenantId, f.Worker.Id, correction.WorkDate, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new AttendanceRecord { ActualStart = null, Status = AttendanceRecord.StatusNotClockedIn, WorkedMinutes = 0 });
        f.Sender.Setup(s => s.Send(It.IsAny<ApproveAttendanceCorrectionCommand>(), It.IsAny<CancellationToken>()))
            .Callback(() => correction.Status = AttendanceCorrection.StatusApproved)
            .ReturnsAsync(Result<AttendanceCorrectionResponse>.Success(null!));

        var result = await f.Provider().ExecuteActionAsync(f.TenantId, "correction", correction.Id, "approve",
            JsonSerializer.SerializeToElement(new { comment = "ok" }), f.Approver.Id, default);

        result.IsSuccess.Should().BeTrue(result.Error);
        var row = f.Journal.Entries.Should().ContainSingle().Subject;
        var before = AttendanceDecisionSnapshot.Parse(row.BeforeJson)!;
        before.Status.Should().Be("pending");
        before.Record!.Status.Should().Be(AttendanceRecord.StatusNotClockedIn);
        AttendanceDecisionSnapshot.Parse(row.AfterJson)!.Status.Should().Be("approved");
    }

    // ---- revert -----------------------------------------------------------------------------

    private static void Journal(Fixture f, string kind, Guid id, AttendanceDecisionSnapshot before, string after)
        => f.Journal.Add(f.TenantId, new ItemRef("attendance", kind, id), "approve", f.Approver.Id, Now.AddMinutes(-5),
            new AttendanceDecisionSnapshot("t", after).ToJson()).BeforeJson = before.ToJson();

    [Fact]
    public async Task Undoing_a_correction_restores_the_attendance_day()
    {
        var f = new Fixture();
        var correction = f.Correction(AttendanceCorrection.StatusApproved);
        correction.ApprovedClockInAt = correction.RequestedClockInAt;
        var record = new AttendanceRecord { ActualStart = correction.RequestedClockInAt, WorkedMinutes = 400, Status = AttendanceRecord.StatusOnTime };
        f.Corrections.Setup(x => x.GetByIdAsync(f.TenantId, correction.Id, It.IsAny<CancellationToken>())).ReturnsAsync(correction);
        f.Corrections.Setup(x => x.GetTrackedByIdAsync(f.TenantId, correction.Id, It.IsAny<CancellationToken>())).ReturnsAsync(correction);
        f.Corrections.Setup(x => x.ListMyAsync(f.TenantId, f.Worker.Id, correction.WorkDate, correction.WorkDate, "approved", 0, 100, It.IsAny<CancellationToken>()))
            .ReturnsAsync((new[] { correction }, 1));
        f.Attendance.Setup(x => x.GetTrackedRecordAsync(f.TenantId, f.Worker.Id, correction.WorkDate, It.IsAny<CancellationToken>())).ReturnsAsync(record);
        Journal(f, "correction", correction.Id, new AttendanceDecisionSnapshot("t", "pending",
            new AttendanceRecordSnapshot(null, null, 0, 0, null, AttendanceRecord.StatusNotClockedIn, [])), "approved");

        var result = await new AttendanceCorrectionReverter(f.Journal, new FakeUnitOfWork(), f.Clock, f.CurrentUser.Object,
                f.Corrections.Object, f.Attendance.Object, f.Provider())
            .RevertAsync(f.TenantId, new ItemRef("attendance", "correction", correction.Id), f.Approver.Id, null, default);

        result.IsSuccess.Should().BeTrue(result.Error);
        (record.ActualStart, record.WorkedMinutes, record.Status).Should().Be(((DateTimeOffset?)null, 0, AttendanceRecord.StatusNotClockedIn));
        correction.Status.Should().Be(AttendanceCorrection.StatusPending);
        correction.ApprovedClockInAt.Should().BeNull();
    }

    [Fact]
    public async Task A_correction_cannot_be_undone_once_a_newer_one_was_applied_to_the_same_day()
    {
        var f = new Fixture();
        var correction = f.Correction(AttendanceCorrection.StatusApproved);
        var newer = f.Correction(AttendanceCorrection.StatusApproved);
        newer.ReviewedAt = Now.AddMinutes(-1);
        f.Corrections.Setup(x => x.GetByIdAsync(f.TenantId, correction.Id, It.IsAny<CancellationToken>())).ReturnsAsync(correction);
        f.Corrections.Setup(x => x.ListMyAsync(f.TenantId, f.Worker.Id, correction.WorkDate, correction.WorkDate, "approved", 0, 100, It.IsAny<CancellationToken>()))
            .ReturnsAsync((new[] { correction, newer }, 2));
        Journal(f, "correction", correction.Id, new AttendanceDecisionSnapshot("t", "pending"), "approved");

        var result = await new AttendanceCorrectionReverter(f.Journal, new FakeUnitOfWork(), f.Clock, f.CurrentUser.Object,
                f.Corrections.Object, f.Attendance.Object, f.Provider())
            .RevertAsync(f.TenantId, new ItemRef("attendance", "correction", correction.Id), f.Approver.Id, null, default);

        result.StatusCode.Should().Be(409);
        result.Error.Should().Be(AttendanceCorrectionReverter.NewerCorrection);
    }

    [Fact]
    public async Task A_device_approval_cannot_be_undone_once_the_new_device_was_used_and_otherwise_reactivates_the_old_one()
    {
        var f = new Fixture();
        var oldDevice = Guid.NewGuid();
        var request = new DeviceChangeRequest
        {
            Id = Guid.NewGuid(), TenantId = f.TenantId, EmployeeId = f.Worker.Id, CurrentDeviceRegistrationId = oldDevice,
            Status = DeviceChangeRequest.StatusApproved, RequestedAt = Now.AddHours(-1),
        };
        f.Devices.Setup(x => x.GetTrackedByIdAsync(f.TenantId, request.Id, It.IsAny<CancellationToken>())).ReturnsAsync(request);
        Journal(f, "device_change", request.Id, new AttendanceDecisionSnapshot("t", "pending", OldDeviceRegistrationId: oldDevice, OldDeviceActive: true), "approved");
        AttendanceDecisionReverterBaseShim(out var reverter, f);

        f.Tray.Setup(x => x.FindActiveDeviceForUserAsync(f.Worker.UserId, f.TenantId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new TrayDeviceRegistration { Id = Guid.NewGuid(), ActivatedAt = Now.AddMinutes(-2) });
        var blocked = await reverter.RevertAsync(f.TenantId, new ItemRef("attendance", "device_change", request.Id), f.Approver.Id, null, default);

        f.Tray.Setup(x => x.FindActiveDeviceForUserAsync(f.Worker.UserId, f.TenantId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((TrayDeviceRegistration?)null);
        var undone = await reverter.RevertAsync(f.TenantId, new ItemRef("attendance", "device_change", request.Id), f.Approver.Id, null, default);

        blocked.Error.Should().Be(DeviceChangeReverter.NewDeviceUsed);
        undone.IsSuccess.Should().BeTrue(undone.Error);
        f.Tray.Verify(x => x.ReactivateDeviceAsync(oldDevice, It.IsAny<CancellationToken>()), Times.Once);
        request.Status.Should().Be(DeviceChangeRequest.StatusPending);
    }

    private static void AttendanceDecisionReverterBaseShim(out DeviceChangeReverter reverter, Fixture f)
        => reverter = new DeviceChangeReverter(f.Journal, new FakeUnitOfWork(), f.Clock, f.CurrentUser.Object, f.Devices.Object, f.Tray.Object, f.Employees.Object);

    [Fact]
    public async Task An_applied_location_change_is_undone_to_the_previous_location_only_while_it_is_still_the_latest()
    {
        var f = new Fixture();
        var appliedAt = Now.AddMinutes(-3);
        var request = new LocationChangeRequest
        {
            Id = Guid.NewGuid(), TenantId = f.TenantId, EmployeeId = f.Worker.Id, LegalEntityId = f.LegalEntityId,
            RequestedLatitude = 7.1, RequestedLongitude = 80.1, Status = LocationChangeRequest.StatusApplied, AppliedAt = appliedAt, RequestedAt = Now.AddHours(-1),
        };
        var current = new EmployeeWorkLocation { EmployeeId = f.Worker.Id, Latitude = 7.1, Longitude = 80.1, UpdatedAt = Now.AddMinutes(-1) };
        f.Locations.Setup(x => x.GetByIdAsync(f.TenantId, request.Id, It.IsAny<CancellationToken>())).ReturnsAsync(request);
        f.Locations.Setup(x => x.GetTrackedByIdAsync(f.TenantId, request.Id, It.IsAny<CancellationToken>())).ReturnsAsync(request);
        f.WorkLocations.Setup(x => x.GetByEmployeeIdAsync(f.TenantId, f.Worker.Id, It.IsAny<CancellationToken>())).ReturnsAsync(current);
        Journal(f, "location_change", request.Id, new AttendanceDecisionSnapshot("t", "pending",
            WorkLocation: new WorkLocationSnapshot(6.9, 79.8, 15, Now.AddDays(-30))), "approved");
        var reverter = new LocationChangeReverter(f.Journal, new FakeUnitOfWork(), f.Clock, f.CurrentUser.Object, f.Locations.Object, f.WorkLocations.Object);
        var item = new ItemRef("attendance", "location_change", request.Id);

        var blocked = await reverter.RevertAsync(f.TenantId, item, f.Approver.Id, null, default); // another change was applied later
        current.UpdatedAt = appliedAt;
        var undone = await reverter.RevertAsync(f.TenantId, item, f.Approver.Id, null, default);

        blocked.Error.Should().Be(LocationChangeReverter.NewerLocation);
        undone.IsSuccess.Should().BeTrue(undone.Error);
        (current.Latitude, current.Longitude, current.AccuracyMeters).Should().Be((6.9, 79.8, (double?)15));
        (request.Status, request.AppliedAt).Should().Be((LocationChangeRequest.StatusPending, (DateTimeOffset?)null));
    }

    [Fact]
    public async Task A_work_mode_approval_cannot_be_undone_once_the_past_day_was_worked_under_it()
    {
        var f = new Fixture();
        var request = new WorkAreaChangeRequest
        {
            Id = Guid.NewGuid(), TenantId = f.TenantId, EmployeeId = f.Worker.Id, LegalEntityId = f.LegalEntityId, Date = new DateOnly(2026, 10, 12),
            Status = WorkAreaChangeRequest.StatusApproved, RequestedAt = Now.AddDays(-3),
        };
        f.WorkAreas.Setup(x => x.GetByIdAsync(f.TenantId, request.Id, It.IsAny<CancellationToken>())).ReturnsAsync(request);
        f.Attendance.Setup(x => x.GetRecordAsync(f.TenantId, f.Worker.Id, request.Date, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new AttendanceRecord { ActualStart = Now.AddDays(-2) });
        Journal(f, "work_mode_change", request.Id, new AttendanceDecisionSnapshot("t", "pending"), "approved");

        var result = await new WorkModeChangeReverter(f.Journal, new FakeUnitOfWork(), f.Clock, f.CurrentUser.Object, f.WorkAreas.Object, f.Attendance.Object)
            .RevertAsync(f.TenantId, new ItemRef("attendance", "work_mode_change", request.Id), f.Approver.Id, null, default);

        result.Error.Should().Be(WorkModeChangeReverter.AlreadyWorked);
    }

    // ---- attendance alerts (plan E2) -------------------------------------------------------------

    private static JsonElement Body(object value) => JsonSerializer.SerializeToElement(value);

    private static ItemRef AlertRef(ONEVO.Domain.Features.TimeAttendance.Alerts.AttendanceAlert alert)
        => new(AttendanceInboxKinds.Source, AttendanceInboxKinds.Alert, alert.Id);

    [Fact]
    public async Task Alerts_are_received_only_for_employees_in_the_reviewers_scope_and_the_employee_sees_their_own_as_info()
    {
        var f = new Fixture();
        var other = Guid.NewGuid();
        var inScope = f.Alert(f.Worker.Id);
        f.OpenAlerts.AddRange([inScope, f.Alert(other)]);
        f.Scope.Add(f.Worker.Id);
        var own = f.Alert(f.Approver.Id, ONEVO.Domain.Features.TimeAttendance.Alerts.AttendanceAlertSignals.LateClockIns);
        f.OwnAlerts.Add(own);

        var items = await f.Provider().ListAsync(f.TenantId, new InboxQuery(Category: [InboxCategories.Alert]), f.Approver.Id, default);

        items.Should().HaveCount(2);
        var received = items.Single(i => i.Id == inScope.Id);
        (received.Direction, received.IsPending, received.Category, received.AvailableActions.Single().Key)
            .Should().Be((InboxDirections.Received, true, InboxCategories.Alert, AttendanceInboxActions.Resolve));
        received.AvailableActions.Single().Meta!["outcomes"].Should().Contain(AttendanceAlertResolveOutcomes.ConvertToLeave);
        received.Facets["signal_key"].Should().Equal(ONEVO.Domain.Features.TimeAttendance.Alerts.AttendanceAlertSignals.AbsentDays);
        var info = items.Single(i => i.Id == own.Id);
        (info.Direction, info.IsPending, info.AvailableActions.Count).Should().Be((InboxDirections.Info, false, 0));
        info.AvailableActions.Should().BeEmpty();
    }

    [Fact]
    public async Task Without_attendance_approve_no_alert_is_received_and_resolving_is_refused()
    {
        var f = new Fixture();
        f.CurrentUser.Setup(x => x.HasPermission("attendance:approve")).Returns(false);
        var alert = f.Alert(f.Worker.Id);
        f.OpenAlerts.Add(alert);
        f.Scope.Add(f.Worker.Id);

        (await f.Provider().ListAsync(f.TenantId, new InboxQuery(Category: [InboxCategories.Alert]), f.Approver.Id, default)).Should().BeEmpty();
        var result = await f.Provider().ExecuteActionAsync(f.TenantId, AttendanceInboxKinds.Alert, alert.Id, AttendanceInboxActions.Resolve,
            Body(new { outcome = "excused" }), f.Approver.Id, default);
        result.StatusCode.Should().Be(403);
        alert.Status.Should().Be(ONEVO.Domain.Features.TimeAttendance.Alerts.AttendanceAlertStatuses.Open);
    }

    [Fact]
    public async Task Resolving_out_of_scope_is_refused_and_convert_to_leave_is_for_absences_only()
    {
        var f = new Fixture();
        var outOfScope = f.Alert(f.Worker.Id);
        f.OpenAlerts.Add(outOfScope);
        (await f.Provider().ExecuteActionAsync(f.TenantId, AttendanceInboxKinds.Alert, outOfScope.Id, AttendanceInboxActions.Resolve,
            Body(new { outcome = "excused" }), f.Approver.Id, default)).StatusCode.Should().Be(403);

        f.Scope.Add(f.Worker.Id);
        var late = f.Alert(f.Worker.Id, ONEVO.Domain.Features.TimeAttendance.Alerts.AttendanceAlertSignals.LateClockIns);
        f.OpenAlerts.Add(late);
        var converted = await f.Provider().ExecuteActionAsync(f.TenantId, AttendanceInboxKinds.Alert, late.Id, AttendanceInboxActions.Resolve,
            Body(new { outcome = "convert_to_leave", leaveTypeId = Guid.NewGuid().ToString() }), f.Approver.Id, default);
        converted.StatusCode.Should().Be(422);
        converted.Error.Should().Be(AttendanceAlertInbox.ConvertOnlyAbsence);
        (await f.Provider().ExecuteActionAsync(f.TenantId, AttendanceInboxKinds.Alert, late.Id, AttendanceInboxActions.Resolve,
            Body(new { outcome = "ignored" }), f.Approver.Id, default)).StatusCode.Should().Be(422);
        f.Journal.Entries.Should().BeEmpty();
    }

    [Theory]
    [InlineData("excused")]
    [InlineData("confirmed")]
    public async Task Resolving_marks_the_alert_and_journals_the_review_state_before_and_after(string outcome)
    {
        var f = new Fixture();
        var alert = f.Alert(f.Worker.Id);
        f.OpenAlerts.Add(alert);
        f.Scope.Add(f.Worker.Id);

        var result = await f.Provider().ExecuteActionAsync(f.TenantId, AttendanceInboxKinds.Alert, alert.Id, AttendanceInboxActions.Resolve,
            Body(new { outcome, comment = "Doctor's note" }), f.Approver.Id, default);

        result.IsSuccess.Should().BeTrue(result.Error);
        result.Value!.AffectedEmployeeIds.Should().BeEquivalentTo([f.Worker.Id, f.Approver.Id]);
        (alert.Status, alert.Outcome, alert.Note, alert.ResolvedByEmployeeId, alert.ResolvedAt)
            .Should().Be((ONEVO.Domain.Features.TimeAttendance.Alerts.AttendanceAlertStatuses.Resolved, outcome, "Doctor's note", (Guid?)f.Approver.Id, (DateTimeOffset?)Now));
        var entry = f.Journal.Entries.Should().ContainSingle().Subject;
        entry.Action.Should().Be(InboxJournalActions.Resolve(outcome));
        AttendanceAlertSnapshot.Parse(entry.BeforeJson)!.Status.Should().Be(ONEVO.Domain.Features.TimeAttendance.Alerts.AttendanceAlertStatuses.Open);
        AttendanceAlertSnapshot.Parse(entry.AfterJson)!.Outcome.Should().Be(outcome);
        f.Alerts.Verify(x => x.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Converting_an_absence_runs_the_leave_modules_command_and_links_the_leave()
    {
        var f = new Fixture();
        var alert = f.Alert(f.Worker.Id);
        f.OpenAlerts.Add(alert);
        f.Scope.Add(f.Worker.Id);
        var leaveType = Guid.NewGuid();
        var leaveId = Guid.NewGuid();
        ONEVO.Application.Features.Leave.Approval.Commands.CreateApproverBackdatedLeaveCommand? sent = null;
        f.Sender.Setup(s => s.Send(It.IsAny<ONEVO.Application.Features.Leave.Approval.Commands.CreateApproverBackdatedLeaveCommand>(), It.IsAny<CancellationToken>()))
            .Callback((IRequest<Result<Guid>> c, CancellationToken _) => sent = (ONEVO.Application.Features.Leave.Approval.Commands.CreateApproverBackdatedLeaveCommand)c)
            .ReturnsAsync(Result<Guid>.Success(leaveId));

        var result = await f.Provider().ExecuteActionAsync(f.TenantId, AttendanceInboxKinds.Alert, alert.Id, AttendanceInboxActions.Resolve,
            Body(new { outcome = "convert_to_leave", leaveTypeId = leaveType.ToString() }), f.Approver.Id, default);

        result.IsSuccess.Should().BeTrue(result.Error);
        sent.Should().Be(new ONEVO.Application.Features.Leave.Approval.Commands.CreateApproverBackdatedLeaveCommand(f.Worker.Id, leaveType, alert.WorkDate, null));
        (alert.Outcome, alert.ConvertedLeaveRequestId).Should().Be((ONEVO.Domain.Features.TimeAttendance.Alerts.AttendanceAlertOutcomes.ConvertedToLeave, (Guid?)leaveId));
        f.Journal.Entries.Single().Action.Should().Be(InboxJournalActions.Resolve(ONEVO.Domain.Features.TimeAttendance.Alerts.AttendanceAlertOutcomes.ConvertedToLeave));
    }

    [Fact]
    public async Task A_refused_leave_conversion_leaves_the_alert_open_with_the_leave_modules_message()
    {
        var f = new Fixture();
        var alert = f.Alert(f.Worker.Id);
        f.OpenAlerts.Add(alert);
        f.Scope.Add(f.Worker.Id);
        f.Sender.Setup(s => s.Send(It.IsAny<ONEVO.Application.Features.Leave.Approval.Commands.CreateApproverBackdatedLeaveCommand>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<Guid>.UnprocessableEntity("Insufficient leave balance."));

        var result = await f.Provider().ExecuteActionAsync(f.TenantId, AttendanceInboxKinds.Alert, alert.Id, AttendanceInboxActions.Resolve,
            Body(new { outcome = "convert_to_leave", leaveTypeId = Guid.NewGuid().ToString() }), f.Approver.Id, default);

        (result.StatusCode, result.Error).Should().Be((422, "Insufficient leave balance."));
        alert.Status.Should().Be(ONEVO.Domain.Features.TimeAttendance.Alerts.AttendanceAlertStatuses.Open);
        alert.ConvertedLeaveRequestId.Should().BeNull();
        f.Journal.Entries.Should().BeEmpty();
    }

    private static AttendanceAlertReverter AlertReverter(Fixture f)
        => new(f.Journal, new FakeUnitOfWork(), f.Clock, f.Alerts.Object, f.LeaveApprovals.Object, f.AlertInbox(), f.Sender.Object);

    [Fact]
    public async Task Undoing_a_resolution_puts_the_alert_back_to_open()
    {
        var f = new Fixture();
        var alert = f.Alert(f.Worker.Id);
        f.OpenAlerts.Add(alert);
        f.Scope.Add(f.Worker.Id);
        await f.Provider().ExecuteActionAsync(f.TenantId, AttendanceInboxKinds.Alert, alert.Id, AttendanceInboxActions.Resolve,
            Body(new { outcome = "excused" }), f.Approver.Id, default);

        var result = await AlertReverter(f).RevertAsync(f.TenantId, AlertRef(alert), f.Approver.Id, null, default);

        result.IsSuccess.Should().BeTrue(result.Error);
        (alert.Status, alert.Outcome, alert.ResolvedByEmployeeId, alert.ResolvedAt)
            .Should().Be((ONEVO.Domain.Features.TimeAttendance.Alerts.AttendanceAlertStatuses.Open, (string?)null, (Guid?)null, (DateTimeOffset?)null));
    }

    [Fact]
    public async Task Undoing_a_conversion_cancels_the_created_leave_and_is_blocked_once_that_leave_changed()
    {
        var f = new Fixture();
        var alert = f.Alert(f.Worker.Id);
        f.OpenAlerts.Add(alert);
        f.Scope.Add(f.Worker.Id);
        var leave = new ONEVO.Domain.Features.Leave.Request.Entities.LeaveRequest
        {
            Id = Guid.NewGuid(), TenantId = f.TenantId, EmployeeId = f.Worker.Id, Status = ONEVO.Domain.Features.Leave.Common.LeaveRequestStatuses.Approved,
            UpdatedAt = Now,
        };
        f.LeaveApprovals.Setup(x => x.GetStateAsync(f.TenantId, leave.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(() => new ONEVO.Application.Features.Leave.Approval.RepositoryInterfaces.LeaveApprovalState(leave, null, f.Worker, "Annual", "AL", "any_one", []));
        f.Sender.Setup(s => s.Send(It.IsAny<ONEVO.Application.Features.Leave.Approval.Commands.CreateApproverBackdatedLeaveCommand>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<Guid>.Success(leave.Id));
        ONEVO.Application.Features.Leave.Cancellation.Commands.CancelLeaveRequestCommand? cancel = null;
        f.Sender.Setup(s => s.Send(It.IsAny<ONEVO.Application.Features.Leave.Cancellation.Commands.CancelLeaveRequestCommand>(), It.IsAny<CancellationToken>()))
            .Callback((IRequest<Result<ONEVO.Application.Features.Leave.Cancellation.DTOs.Responses.CancelLeaveRequestResponse>> c, CancellationToken _)
                => cancel = (ONEVO.Application.Features.Leave.Cancellation.Commands.CancelLeaveRequestCommand)c)
            .ReturnsAsync(Result<ONEVO.Application.Features.Leave.Cancellation.DTOs.Responses.CancelLeaveRequestResponse>.Success(null!));
        await f.Provider().ExecuteActionAsync(f.TenantId, AttendanceInboxKinds.Alert, alert.Id, AttendanceInboxActions.Resolve,
            Body(new { outcome = "convert_to_leave", leaveTypeId = Guid.NewGuid().ToString() }), f.Approver.Id, default);

        leave.UpdatedAt = Now.AddMinutes(5); // HR touched the leave since
        var blocked = await AlertReverter(f).RevertAsync(f.TenantId, AlertRef(alert), f.Approver.Id, null, default);
        blocked.Error.Should().Be(AttendanceAlertReverter.LeaveChangedSince);
        cancel.Should().BeNull();

        leave.UpdatedAt = Now;
        var result = await AlertReverter(f).RevertAsync(f.TenantId, AlertRef(alert), f.Approver.Id, null, default);

        result.IsSuccess.Should().BeTrue(result.Error);
        cancel.Should().Be(new ONEVO.Application.Features.Leave.Cancellation.Commands.CancelLeaveRequestCommand(
            leave.Id, AttendanceAlertReverter.RevertReason, null, null, RevertOfApproverBackdatedLeave: true));
        (alert.Status, alert.ConvertedLeaveRequestId).Should().Be((ONEVO.Domain.Features.TimeAttendance.Alerts.AttendanceAlertStatuses.Open, (Guid?)null));
    }

    // ---- final review fixes -----------------------------------------------------------------------

    [Fact]
    public async Task Undoing_a_conversion_is_allowed_when_the_stored_leave_time_only_lost_its_sub_microsecond_ticks()
    {
        var f = new Fixture();
        var alert = f.Alert(f.Worker.Id);
        f.OpenAlerts.Add(alert);
        f.Scope.Add(f.Worker.Id);
        var leave = new ONEVO.Domain.Features.Leave.Request.Entities.LeaveRequest
        {
            Id = Guid.NewGuid(), TenantId = f.TenantId, EmployeeId = f.Worker.Id, Status = ONEVO.Domain.Features.Leave.Common.LeaveRequestStatuses.Approved,
            UpdatedAt = Now.AddTicks(3), // the in-memory value the save interceptor set (100 ns precision)
        };
        f.LeaveApprovals.Setup(x => x.GetStateAsync(f.TenantId, leave.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(() => new ONEVO.Application.Features.Leave.Approval.RepositoryInterfaces.LeaveApprovalState(leave, null, f.Worker, "Annual", "AL", "any_one", []));
        f.Sender.Setup(s => s.Send(It.IsAny<ONEVO.Application.Features.Leave.Approval.Commands.CreateApproverBackdatedLeaveCommand>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<Guid>.Success(leave.Id));
        f.Sender.Setup(s => s.Send(It.IsAny<ONEVO.Application.Features.Leave.Cancellation.Commands.CancelLeaveRequestCommand>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<ONEVO.Application.Features.Leave.Cancellation.DTOs.Responses.CancelLeaveRequestResponse>.Success(null!));
        await f.Provider().ExecuteActionAsync(f.TenantId, AttendanceInboxKinds.Alert, alert.Id, AttendanceInboxActions.Resolve,
            Body(new { outcome = "convert_to_leave", leaveTypeId = Guid.NewGuid().ToString() }), f.Approver.Id, default);

        leave.UpdatedAt = Now; // read back from Postgres timestamptz: microseconds only
        var result = await AlertReverter(f).RevertAsync(f.TenantId, AlertRef(alert), f.Approver.Id, null, default);

        result.IsSuccess.Should().BeTrue(result.Error);
    }

    [Fact]
    public async Task Received_alerts_are_scoped_before_the_list_cap_so_older_out_of_scope_alerts_do_not_crowd_out_the_team()
    {
        var f = new Fixture();
        for (var i = 0; i < AttendanceAlertInbox.ReceivedLimit + 100; i++)
        {
            var old = f.Alert(Guid.NewGuid());
            old.WorkDate = new DateOnly(2026, 8, 1);
            f.OpenAlerts.Add(old);
        }
        var mine = f.Alert(f.Worker.Id);
        f.OpenAlerts.Add(mine);
        f.Scope.Add(f.Worker.Id);

        var items = await f.Provider().ListAsync(f.TenantId, new InboxQuery(Category: [InboxCategories.Alert]), f.Approver.Id, default);

        items.Select(i => i.Id).Should().Equal(mine.Id);
    }

    [Fact]
    public async Task Attendance_read_outside_the_callers_coverage_opens_neither_a_request_nor_an_alert()
    {
        var f = new Fixture();
        f.CurrentUser.Setup(x => x.HasPermission("attendance:approve")).Returns(false);
        f.CurrentUser.Setup(x => x.HasPermission("attendance:read")).Returns(true);
        var correction = f.Correction();
        f.Corrections.Setup(x => x.GetByIdAsync(f.TenantId, correction.Id, It.IsAny<CancellationToken>())).ReturnsAsync(correction);
        var alert = f.Alert(f.Worker.Id);
        f.OpenAlerts.Add(alert);
        f.Authority.Setup(a => a.ResolveVisibilityAsync(It.IsAny<EmployeeAuthorityVisibilityRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new EmployeeAuthorityVisibilityScope(f.ApproverUserId, f.LegalEntityId, false, []));

        (await f.Provider().GetDetailAsync(f.TenantId, "correction", correction.Id, f.Approver.Id, default)).Should().BeNull();
        (await f.Provider().GetDetailAsync(f.TenantId, AttendanceInboxKinds.Alert, alert.Id, f.Approver.Id, default)).Should().BeNull();

        f.Authority.Setup(a => a.ResolveVisibilityAsync(It.IsAny<EmployeeAuthorityVisibilityRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new EmployeeAuthorityVisibilityScope(f.ApproverUserId, f.LegalEntityId, false, [f.Worker.Id]));
        (await f.Provider().GetDetailAsync(f.TenantId, "correction", correction.Id, f.Approver.Id, default)).Should().NotBeNull();
        (await f.Provider().GetDetailAsync(f.TenantId, AttendanceInboxKinds.Alert, alert.Id, f.Approver.Id, default)).Should().NotBeNull();
        f.Authority.Verify(a => a.ResolveVisibilityAsync(It.Is<EmployeeAuthorityVisibilityRequest>(r =>
            r.RequiredPermission == "attendance:read" && r.LegalEntityId == f.LegalEntityId), It.IsAny<CancellationToken>()), Times.AtLeastOnce);
    }
}
