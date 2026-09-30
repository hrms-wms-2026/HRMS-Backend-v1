using FluentAssertions;
using Moq;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.CoreHr.Employee.DTOs.Responses;
using ONEVO.Application.Features.CoreHr.Employee.Queries.GetEmployeeApprovalActivity;
using ONEVO.Application.Features.CoreHr.Employee.ServiceInterfaces;
using ONEVO.Application.Features.Leave.Request.RepositoryInterfaces;
using ONEVO.Application.Features.TimeAttendance.RepositoryInterfaces;
using ONEVO.Application.Features.WorkManagement.Approvals.RepositoryInterfaces;
using ONEVO.Application.Features.WorkManagement.Common.Services;
using ONEVO.Domain.Features.Leave.Request.Entities;
using ONEVO.Domain.Features.TimeAttendance.Entities;

namespace ONEVO.Tests.Unit.Features.CoreHr.Employee;

public sealed class GetEmployeeApprovalActivityQueryHandlerTests
{
    private readonly Mock<IEmployeeReadAccessGuard> _guard = new();
    private readonly Mock<ILeaveRequestRepository> _leave = new();
    private readonly Mock<IAttendanceCorrectionRepository> _corrections = new();
    private readonly Mock<IWorkAreaChangeRequestRepository> _workAreas = new();
    private readonly Mock<ILocationChangeRequestRepository> _locations = new();
    private readonly Mock<IWorkApprovalHistoryRepository> _workApprovals = new();
    private readonly Mock<ICallerIdentityResolver> _identity = new();
    private readonly Mock<ICurrentUser> _user = new();
    private readonly Mock<IDateTimeProvider> _clock = new();
    private readonly Guid _tenantId = Guid.NewGuid();
    private readonly Guid _userId = Guid.NewGuid();
    private readonly Guid _employeeId = Guid.NewGuid();
    private static readonly DateOnly From = new(2026, 9, 1);
    private static readonly DateOnly To = new(2026, 9, 30);

    public GetEmployeeApprovalActivityQueryHandlerTests()
    {
        _user.SetupGet(u => u.TenantId).Returns(_tenantId);
        _user.SetupGet(u => u.UserId).Returns(_userId);
        _user.Setup(u => u.HasPermission(It.IsAny<string>())).Returns(true);
        _clock.SetupGet(c => c.Today).Returns(new DateOnly(2026, 9, 21));
        _guard.Setup(g => g.EnsureCanRead(_tenantId, _employeeId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<EmployeeListItemResponse>.Success(new EmployeeListItemResponse(
                _employeeId, "E-001", "Ada", "ada@test.dev", null, null, null, null, null, null, "full_time", "active", null, null)));

        _leave.Setup(l => l.ListOwnAsync(_tenantId, _employeeId, It.IsAny<LeaveRequestListFilter>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Array.Empty<LeaveRequestListRow>());
        ArrangeCorrections();
        ArrangeWorkAreas();
        ArrangeLocations();
        _workApprovals.Setup(w => w.ListRequestedByEmployeeAsync(_tenantId, _employeeId, It.IsAny<DateTimeOffset>(), It.IsAny<DateTimeOffset>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Array.Empty<WorkApprovalHistoryRecord>());
        _identity.Setup(i => i.ResolveDisplayNamesByEmployeeIdAsync(_tenantId, It.IsAny<IReadOnlyList<Guid>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Dictionary<Guid, string>());
    }

    private GetEmployeeApprovalActivityQueryHandler CreateHandler() =>
        new(_guard.Object, _leave.Object, _corrections.Object, _workAreas.Object,
            _locations.Object, _workApprovals.Object, _identity.Object, _user.Object, _clock.Object);

    private void ArrangeCorrections(params AttendanceCorrection[] items) =>
        _corrections.Setup(c => c.ListMyAsync(_tenantId, _employeeId, null, null, null, 0, It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(((IReadOnlyList<AttendanceCorrection>)items, items.Length));

    private void ArrangeWorkAreas(params WorkAreaChangeRequest[] items) =>
        _workAreas.Setup(w => w.ListMyAsync(_tenantId, _employeeId, null, null, null, 0, It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(((IReadOnlyList<WorkAreaChangeRequest>)items, items.Length));

    private void ArrangeLocations(params LocationChangeRequest[] items) =>
        _locations.Setup(l => l.ListMyAsync(_tenantId, _employeeId, null, 0, It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(((IReadOnlyList<LocationChangeRequest>)items, items.Length));

    private void ArrangeWork(params WorkApprovalHistoryRecord[] records) =>
        _workApprovals.Setup(w => w.ListRequestedByEmployeeAsync(_tenantId, _employeeId, It.IsAny<DateTimeOffset>(), It.IsAny<DateTimeOffset>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(records);

    private void ArrangeLeave(params (string status, string created)[] items) =>
        _leave.Setup(l => l.ListOwnAsync(_tenantId, _employeeId, It.IsAny<LeaveRequestListFilter>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(items.Select(i => new LeaveRequestListRow(
                new LeaveRequest
                {
                    Id = Guid.NewGuid(), TenantId = _tenantId, EmployeeId = _employeeId, LeaveTypeId = Guid.NewGuid(),
                    Status = i.status, CreatedAt = DateTimeOffset.Parse(i.created),
                    StartAt = DateTimeOffset.Parse("2026-09-23T00:00:00+00:00"), EndAt = DateTimeOffset.Parse("2026-09-25T00:00:00+00:00")
                },
                "Annual leave", "AL")).ToList());

    private static AttendanceCorrection Correction(string status, string created) => new()
    {
        Id = Guid.NewGuid(), Status = status, CreatedAt = DateTimeOffset.Parse(created),
        WorkDate = new DateOnly(2026, 9, 4), CorrectionType = AttendanceCorrection.TypeClockOut
    };

    private static WorkApprovalHistoryRecord Work(string kind, string status, Guid approver, string created, Guid? decidedBy = null) =>
        new(Guid.NewGuid(), Guid.NewGuid(), kind, status, "Checkout", null, Guid.NewGuid(), approver, decidedBy, null,
            DateTimeOffset.Parse(created), null);

    [Fact]
    public async Task Handle_PassesThroughGuardFailure()
    {
        _guard.Setup(g => g.EnsureCanRead(_tenantId, _employeeId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<EmployeeListItemResponse>.Forbidden("no"));

        var result = await CreateHandler().Handle(new GetEmployeeApprovalActivityQuery(_employeeId, null, null), CancellationToken.None);

        result.StatusCode.Should().Be(403);
    }

    [Fact]
    public async Task Handle_DoesNotRequireAnyModulePermission()
    {
        _user.Setup(u => u.HasPermission(It.IsAny<string>())).Returns(false);

        var result = await CreateHandler().Handle(new GetEmployeeApprovalActivityQuery(_employeeId, null, null), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
    }

    [Fact]
    public async Task Handle_MergesAllSources_NewestFirst_AndFiltersToThePeriod()
    {
        ArrangeLeave(("pending", "2026-09-12T09:00:00+00:00"), ("approved", "2026-08-30T09:00:00+00:00")); // second is outside
        ArrangeCorrections(Correction("approved", "2026-09-05T08:00:00+00:00"));
        ArrangeWorkAreas(new WorkAreaChangeRequest
        {
            Id = Guid.NewGuid(), Status = "rejected", RequestedAt = DateTimeOffset.Parse("2026-09-20T08:00:00+00:00"),
            Date = new DateOnly(2026, 9, 21), CurrentWorkModeName = "Onsite", RequestedWorkModeName = "Remote"
        });
        ArrangeLocations(new LocationChangeRequest { Id = Guid.NewGuid(), Status = "applied", RequestedAt = DateTimeOffset.Parse("2026-09-02T08:00:00+00:00") });
        ArrangeWork(Work("task_creation", "pending", Guid.NewGuid(), "2026-09-15T08:00:00+00:00"));

        var result = await CreateHandler().Handle(new GetEmployeeApprovalActivityQuery(_employeeId, From, To), CancellationToken.None);

        var v = result.Value!;
        v.Total.Should().Be(5);
        v.Pending.Should().Be(2);
        v.Approved.Should().Be(2);   // approved correction + applied location change
        v.Rejected.Should().Be(1);
        v.Items.Select(i => i.Kind).Should().Equal("work_area_change", "task_creation", "leave", "attendance_correction", "location_change");
        v.Items.Select(i => i.RequestedAt).Should().BeInDescendingOrder();
    }

    [Fact]
    public async Task Handle_BuildsLabelAndDetailPerKind()
    {
        ArrangeLeave(("pending", "2026-09-12T09:00:00+00:00"));
        ArrangeCorrections(Correction("pending", "2026-09-05T08:00:00+00:00"));
        ArrangeWorkAreas(new WorkAreaChangeRequest
        {
            Id = Guid.NewGuid(), Status = "pending", RequestedAt = DateTimeOffset.Parse("2026-09-06T08:00:00+00:00"),
            Date = new DateOnly(2026, 9, 21), CurrentWorkModeName = "Onsite", RequestedWorkModeName = "Remote"
        });

        var result = await CreateHandler().Handle(new GetEmployeeApprovalActivityQuery(_employeeId, From, To), CancellationToken.None);

        var byKind = result.Value!.Items.ToDictionary(i => i.Kind);
        byKind["leave"].Label.Should().Be("Leave request");
        byKind["leave"].Detail.Should().Be("Annual leave · 2026-09-23 → 2026-09-25");
        byKind["attendance_correction"].Label.Should().Be("Attendance correction");
        byKind["attendance_correction"].Detail.Should().Be("2026-09-04 · clock out");
        byKind["work_area_change"].Label.Should().Be("Work area change");
        byKind["work_area_change"].Detail.Should().Be("2026-09-21 · Onsite → Remote");
    }

    [Theory]
    [InlineData("accepted", "approved")]
    [InlineData("applied", "approved")]
    [InlineData("declined", "rejected")]
    [InlineData("expired", "cancelled")]
    [InlineData("outdated", "cancelled")]
    [InlineData("something_new", "cancelled")]
    public async Task Handle_NormalisesStatuses(string raw, string expected)
    {
        ArrangeWork(Work("task_status_change", raw, Guid.NewGuid(), "2026-09-10T08:00:00+00:00"));

        var result = await CreateHandler().Handle(new GetEmployeeApprovalActivityQuery(_employeeId, From, To), CancellationToken.None);

        result.Value!.Items.Single().Status.Should().Be(expected);
    }

    [Fact]
    public async Task Handle_NamesTheApproverForWorkRecords_ButNotForInvitations()
    {
        var approver = Guid.NewGuid();
        var decider = Guid.NewGuid();
        ArrangeWork(
            Work("task_creation", "pending", approver, "2026-09-10T08:00:00+00:00"),
            Work("task_edit", "approved", approver, "2026-09-09T08:00:00+00:00", decidedBy: decider),
            Work("objective_invitation", "pending", _employeeId, "2026-09-08T08:00:00+00:00"));
        _identity.Setup(i => i.ResolveDisplayNamesByEmployeeIdAsync(_tenantId, It.IsAny<IReadOnlyList<Guid>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Dictionary<Guid, string> { [approver] = "Abitha Devendran", [decider] = "Grace Hopper" });

        var result = await CreateHandler().Handle(new GetEmployeeApprovalActivityQuery(_employeeId, From, To), CancellationToken.None);

        var byKind = result.Value!.Items.ToDictionary(i => i.Kind);
        byKind["task_creation"].ApproverName.Should().Be("Abitha Devendran");
        byKind["task_edit"].ApproverName.Should().Be("Grace Hopper");
        byKind["project_invitation"].Label.Should().Be("Project invitation");
        byKind["project_invitation"].ApproverName.Should().BeNull();
    }

    [Fact]
    public async Task Handle_ReturnsAtMostTenItems_ButCountsEverything()
    {
        ArrangeWork(Enumerable.Range(1, 12)
            .Select(i => Work("task_edit", i % 2 == 0 ? "approved" : "pending", Guid.NewGuid(), $"2026-09-{i:00}T08:00:00+00:00"))
            .ToArray());

        var result = await CreateHandler().Handle(new GetEmployeeApprovalActivityQuery(_employeeId, From, To), CancellationToken.None);

        result.Value!.Items.Should().HaveCount(10);
        result.Value.Total.Should().Be(12);
        result.Value.Pending.Should().Be(6);
        result.Value.Approved.Should().Be(6);
    }

    [Fact]
    public async Task Handle_Returns400_ForAnInvalidPeriod()
    {
        var result = await CreateHandler().Handle(new GetEmployeeApprovalActivityQuery(_employeeId, To, From), CancellationToken.None);

        result.StatusCode.Should().Be(400);
    }
}
