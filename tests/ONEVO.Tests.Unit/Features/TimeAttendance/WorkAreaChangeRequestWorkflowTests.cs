using FluentAssertions;
using Moq;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Common.RepositoryInterfaces;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.Auth.Login.RepositoryInterfaces;
using ONEVO.Application.Features.CoreHr.EmployeeAuthority.Models;
using ONEVO.Application.Features.CoreHr.EmployeeAuthority.ServiceInterfaces;
using ONEVO.Application.Features.OrgStructure.RepositoryInterfaces;
using ONEVO.Application.Features.TimeAttendance.Commands.WorkAreaChangeRequests;
using ONEVO.Application.Features.TimeAttendance.DTOs.Responses;
using ONEVO.Application.Features.TimeAttendance.Queries.WorkAreaChangeRequests;
using ONEVO.Application.Features.TimeAttendance.RepositoryInterfaces;
using ONEVO.Application.Features.TimeAttendance.Services;
using ONEVO.Domain.Features.Auth.Entities;
using ONEVO.Domain.Features.CoreHr.Entities;
using ONEVO.Domain.Features.OrgStructure.Entities;
using ONEVO.Domain.Features.TimeAttendance.Entities;
using ONEVO.Tests.Unit.Fakes;
using CoreEmployeeRepository = ONEVO.Application.Features.CoreHr.Employee.RepositoryInterfaces.IEmployeeRepository;

namespace ONEVO.Tests.Unit.Features.TimeAttendance;

public sealed class WorkAreaChangeRequestWorkflowTests
{
    [Fact]
    public async Task Preview_ResolvesServerContextAndDoesNotPersistOrNotify()
    {
        var fixture = new Fixture();

        var result = await fixture.Workflow.PreviewAsync(
            new PreviewWorkAreaChangeRequestCommand(fixture.Date, " REMOTE ", "  Appointment  "),
            CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value!.RequestedWorkArea.Should().Be("remote");
        result.Value.Reason.Should().Be("Appointment");
        result.Value.CurrentExpectedWorkArea.Should().Be("onsite");
        result.Value.Timezone.Should().Be("UTC");
        result.Value.Receiver.Should().NotBeNull();
        fixture.Requests.Verify(x => x.AddAsync(It.IsAny<WorkAreaChangeRequest>(), It.IsAny<CancellationToken>()), Times.Never);
        fixture.Notifications.Should().BeEmpty();
        fixture.Authority.Verify(x => x.ResolveApproverAsync(
            It.Is<EmployeeApprovalRouteRequest>(r => r.LegalEntityId == fixture.LegalEntityId
                && r.RequiredPermission == "attendance:approve"
                && r.Purpose == EmployeeAuthorityPurpose.WorkAreaChangeApproval),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Theory]
    [InlineData(-1)]
    public async Task Preview_PastLegalEntityLocalDateIsRejected(int offset)
    {
        var fixture = new Fixture();

        var result = await fixture.Workflow.PreviewAsync(
            new PreviewWorkAreaChangeRequestCommand(fixture.Date.AddDays(offset), "remote", "Reason"),
            CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.StatusCode.Should().Be(409);
    }

    [Fact]
    public async Task Create_StoresPendingRequestWithServerOwnedFieldsAndStagesApproverNotification()
    {
        var fixture = new Fixture();

        var result = await fixture.Workflow.CreateAsync(
            new CreateWorkAreaChangeRequestCommand(fixture.Date, " REMOTE ", "  Appointment  "),
            CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        fixture.AddedRequest.Should().NotBeNull();
        fixture.AddedRequest!.Status.Should().Be(WorkAreaChangeRequest.StatusPending);
        fixture.AddedRequest.TenantId.Should().Be(fixture.TenantId);
        fixture.AddedRequest.EmployeeId.Should().Be(fixture.EmployeeId);
        fixture.AddedRequest.LegalEntityId.Should().Be(fixture.LegalEntityId);
        fixture.AddedRequest.CurrentExpectedWorkArea.Should().Be("onsite");
        fixture.AddedRequest.RequestedWorkArea.Should().Be("remote");
        fixture.AddedRequest.Reason.Should().Be("Appointment");
        fixture.AddedRequest.RequestedAt.Should().Be(fixture.Now);
        fixture.Notifications.Should().ContainSingle(x =>
            x.RecipientUserId == fixture.ApproverUserId
            && x.TemplateCode == "work_area_change_request_created"
            && x.RelatedEntityId == fixture.AddedRequest.Id);
        fixture.UnitOfWork.TransactionCallCount.Should().Be(1);
        fixture.NotificationWasCreatedInsideTransaction.Should().BeTrue();
        fixture.Employees.Verify(x => x.GetDefaultForUserAsync(
            fixture.TenantId, fixture.RequesterUserId, It.IsAny<CancellationToken>()), Times.AtLeastOnce);
    }

    [Fact]
    public async Task Create_DuplicatePrecheckDoesNotPersistOrNotify()
    {
        var fixture = new Fixture();
        fixture.Requests.Setup(x => x.HasActiveForDateAsync(
                fixture.TenantId, fixture.EmployeeId, fixture.Date, It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        var result = await fixture.Workflow.CreateAsync(
            new CreateWorkAreaChangeRequestCommand(fixture.Date, "remote", "Reason"),
            CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.StatusCode.Should().Be(409);
        fixture.AddedRequest.Should().BeNull();
        fixture.Notifications.Should().BeEmpty();
    }

    [Fact]
    public async Task Create_WhenNoApproverExistsDoesNotPersistOrNotify()
    {
        var fixture = new Fixture();
        fixture.Authority.Setup(x => x.ResolveApproverAsync(
                It.IsAny<EmployeeApprovalRouteRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<EmployeeApprovalRoute>.UnprocessableEntity("No approver."));

        var result = await fixture.Workflow.CreateAsync(
            new CreateWorkAreaChangeRequestCommand(fixture.Date, "remote", "Reason"),
            CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.StatusCode.Should().Be(409);
        fixture.AddedRequest.Should().BeNull();
        fixture.Notifications.Should().BeEmpty();
    }

    [Fact]
    public async Task ApprovalInbox_UsesExactApproverScopeBeforeFinalPaging()
    {
        var fixture = new Fixture(actingAsApprover: true);
        var merelyVisible = Guid.NewGuid();
        var exact = fixture.EmployeeId;
        fixture.Requests.Setup(x => x.ListPendingEmployeeIdsAsync(
                fixture.TenantId, fixture.LegalEntityId, null, null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new[] { merelyVisible, exact });
        fixture.Authority.Setup(x => x.ResolveApprovalInboxScopeAsync(
                It.IsAny<EmployeeApprovalInboxScopeRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new[] { exact });
        fixture.Requests.Setup(x => x.ListApprovalInboxAsync(
                fixture.TenantId, fixture.LegalEntityId, It.Is<IReadOnlyCollection<Guid>>(ids => ids.SequenceEqual(new[] { exact })),
                null, null, 0, 20, It.IsAny<CancellationToken>()))
            .ReturnsAsync((new[] { fixture.PendingRequest() }, 1));
        fixture.Attendance.Setup(x => x.ListEmployeeIdentitiesAsync(
                fixture.TenantId, fixture.LegalEntityId, It.IsAny<IReadOnlyCollection<Guid>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Dictionary<Guid, AttendanceHistoryEmployee>
            {
                [fixture.EmployeeId] = new(fixture.EmployeeId, "Requester", "E-1", null, null, null)
            });

        var result = await fixture.Workflow.ListApprovalsAsync(
            new ListWorkAreaChangeRequestApprovalsQuery(null, null, new PagedRequest()), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value!.TotalCount.Should().Be(1);
        fixture.Authority.Verify(x => x.ResolveApprovalInboxScopeAsync(
            It.Is<EmployeeApprovalInboxScopeRequest>(r => r.LegalEntityId == fixture.LegalEntityId
                && r.Purpose == EmployeeAuthorityPurpose.WorkAreaChangeApproval
                && r.RequiredPermission == "attendance:approve"
                && r.CandidateEmployeeIds.Count == 2),
            It.IsAny<CancellationToken>()), Times.Once);
        fixture.Authority.Verify(x => x.ResolveVisibilityAsync(
            It.IsAny<EmployeeAuthorityVisibilityRequest>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Approve_ReResolvesExactRouteAndNotifiesRequester()
    {
        var fixture = new Fixture(actingAsApprover: true);
        var request = fixture.PendingRequest();
        fixture.Requests.Setup(x => x.GetTrackedByIdAsync(fixture.TenantId, request.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(request);

        var result = await fixture.Workflow.ApproveAsync(
            new ApproveWorkAreaChangeRequestCommand(request.Id, "  Approved  "), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        request.Status.Should().Be(WorkAreaChangeRequest.StatusApproved);
        request.ReviewedById.Should().Be(fixture.ApproverUserId);
        request.ReviewComment.Should().Be("Approved");
        fixture.Notifications.Should().ContainSingle(x =>
            x.RecipientUserId == fixture.RequesterUserId
            && x.TemplateCode == "work_area_change_request_decided"
            && x.RelatedEntityId == request.Id);
        fixture.Authority.Verify(x => x.ResolveApproverAsync(
            It.Is<EmployeeApprovalRouteRequest>(r => r.Purpose == EmployeeAuthorityPurpose.WorkAreaChangeApproval),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Approve_NoExistingAttendanceRow_OnlyChangesRequestAndDoesNotTouchAttendanceRepository()
    {
        var fixture = new Fixture(actingAsApprover: true);
        var request = fixture.PendingRequest();
        fixture.Requests.Setup(x => x.GetTrackedByIdAsync(fixture.TenantId, request.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(request);
        fixture.Attendance.Setup(x => x.GetTrackedRecordAsync(
                fixture.TenantId, request.EmployeeId, request.Date, It.IsAny<CancellationToken>()))
            .ReturnsAsync((AttendanceRecord?)null);

        var result = await fixture.Workflow.ApproveAsync(
            new ApproveWorkAreaChangeRequestCommand(request.Id, null), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        request.Status.Should().Be(WorkAreaChangeRequest.StatusApproved);
        fixture.Attendance.Verify(x => x.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Approve_ExistingAttendanceRow_SynchronizesExpectedWorkAreaSnapshotInSameTransaction()
    {
        var fixture = new Fixture(actingAsApprover: true);
        var request = fixture.PendingRequest();
        fixture.Requests.Setup(x => x.GetTrackedByIdAsync(fixture.TenantId, request.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(request);
        var existingRecord = new AttendanceRecord
        {
            Id = Guid.NewGuid(), TenantId = fixture.TenantId, EmployeeId = request.EmployeeId,
            Date = request.Date, ExpectedWorkArea = "onsite",
            ActualStart = fixture.Now.AddHours(-1), Status = AttendanceRecord.StatusActive
        };
        fixture.Attendance.Setup(x => x.GetTrackedRecordAsync(
                fixture.TenantId, request.EmployeeId, request.Date, It.IsAny<CancellationToken>()))
            .ReturnsAsync(existingRecord);

        var result = await fixture.Workflow.ApproveAsync(
            new ApproveWorkAreaChangeRequestCommand(request.Id, null), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        existingRecord.ExpectedWorkArea.Should().Be(request.RequestedWorkArea);
        existingRecord.Status.Should().Be(AttendanceRecord.StatusActive);
        existingRecord.ActualStart.Should().Be(fixture.Now.AddHours(-1));
        fixture.Requests.Verify(x => x.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Reject_LeavesAttendanceSnapshotUnchanged()
    {
        var fixture = new Fixture(actingAsApprover: true);
        var request = fixture.PendingRequest();
        fixture.Requests.Setup(x => x.GetTrackedByIdAsync(fixture.TenantId, request.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(request);
        var existingRecord = new AttendanceRecord
        {
            Id = Guid.NewGuid(), TenantId = fixture.TenantId, EmployeeId = request.EmployeeId,
            Date = request.Date, ExpectedWorkArea = "onsite"
        };
        fixture.Attendance.Setup(x => x.GetTrackedRecordAsync(
                fixture.TenantId, request.EmployeeId, request.Date, It.IsAny<CancellationToken>()))
            .ReturnsAsync(existingRecord);

        var result = await fixture.Workflow.RejectAsync(
            new RejectWorkAreaChangeRequestCommand(request.Id, "Not approved"), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        request.Status.Should().Be(WorkAreaChangeRequest.StatusRejected);
        existingRecord.ExpectedWorkArea.Should().Be("onsite");
        fixture.Attendance.Verify(x => x.GetTrackedRecordAsync(
            It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<DateOnly>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Approve_MerelyVisibleReviewerIsForbidden()
    {
        var fixture = new Fixture(actingAsApprover: true);
        var request = fixture.PendingRequest();
        fixture.Requests.Setup(x => x.GetTrackedByIdAsync(fixture.TenantId, request.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(request);
        fixture.Authority.Setup(x => x.ResolveApproverAsync(
                It.IsAny<EmployeeApprovalRouteRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<EmployeeApprovalRoute>.Success(new EmployeeApprovalRoute(
                Guid.NewGuid(), Guid.NewGuid(), fixture.PositionId, "attendance:approve",
                EmployeeAuthorityPurpose.WorkAreaChangeApproval,
                EmployeeApprovalRouteSource.ReportingLine, null)));

        var result = await fixture.Workflow.ApproveAsync(
            new ApproveWorkAreaChangeRequestCommand(request.Id, null), CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.StatusCode.Should().Be(403);
        request.Status.Should().Be(WorkAreaChangeRequest.StatusPending);
        fixture.Notifications.Should().BeEmpty();
    }

    [Fact]
    public async Task Reject_RequiresCommentAndDoesNotPersistInvalidDecision()
    {
        var fixture = new Fixture(actingAsApprover: true);
        var request = fixture.PendingRequest();
        fixture.Requests.Setup(x => x.GetTrackedByIdAsync(fixture.TenantId, request.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(request);

        var result = await fixture.Workflow.RejectAsync(
            new RejectWorkAreaChangeRequestCommand(request.Id, "  "), CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.StatusCode.Should().Be(400);
        request.Status.Should().Be(WorkAreaChangeRequest.StatusPending);
        fixture.Requests.Verify(x => x.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
        fixture.Notifications.Should().BeEmpty();
    }

    [Fact]
    public async Task Cancel_OnlyRequesterCanCancelAndDoesNotChangePermanentWorkMode()
    {
        var fixture = new Fixture();
        var request = fixture.PendingRequest();
        fixture.Requests.Setup(x => x.GetTrackedByIdAsync(fixture.TenantId, request.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(request);

        var result = await fixture.Workflow.CancelAsync(
            new CancelWorkAreaChangeRequestCommand(request.Id), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        request.Status.Should().Be(WorkAreaChangeRequest.StatusCancelled);
        fixture.Employee.WorkModeId.Should().Be(fixture.OriginalWorkModeId);
        fixture.Attendance.Verify(x => x.GetTrackedRecordAsync(
            It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<DateOnly>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task ListApprovals_PrefersSessionActiveEmployeeOverDefaultForUser()
    {
        var fixture = new Fixture(actingAsApprover: true);
        var otherLegalEntityId = Guid.NewGuid();
        var switchedEmployee = new Employee
        {
            Id = Guid.NewGuid(), TenantId = fixture.TenantId, UserId = fixture.ApproverUserId,
            LegalEntityId = otherLegalEntityId, FirstName = "Sam", LastName = "Approver"
        };
        var switchedLegalEntity = new LegalEntity
        {
            Id = otherLegalEntityId, TenantId = fixture.TenantId, Name = "Other Co", Timezone = "UTC",
            WorkStartTime = new TimeOnly(9), WorkEndTime = new TimeOnly(17), StandardWorkingDays = "[1,2,3,4,5]"
        };
        var sessionId = Guid.NewGuid();

        fixture.CurrentUser.SetupGet(x => x.SessionId).Returns(sessionId);
        fixture.Sessions.Setup(x => x.GetByIdAsync(sessionId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Session
            {
                Id = sessionId, TenantId = fixture.TenantId, UserId = fixture.ApproverUserId,
                ActiveEmployeeId = switchedEmployee.Id, IsRevoked = false, ExpiresAt = DateTimeOffset.MaxValue,
            });
        fixture.Employees.Setup(x => x.GetByIdAsync(fixture.TenantId, switchedEmployee.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(switchedEmployee);
        fixture.LegalEntities.Setup(x => x.GetByIdForTenantAsync(fixture.TenantId, otherLegalEntityId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(switchedLegalEntity);
        fixture.Requests.Setup(x => x.ListPendingEmployeeIdsAsync(
                fixture.TenantId, otherLegalEntityId, null, null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Array.Empty<Guid>());
        fixture.Authority.Setup(x => x.ResolveApprovalInboxScopeAsync(
                It.IsAny<EmployeeApprovalInboxScopeRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Array.Empty<Guid>());
        fixture.Requests.Setup(x => x.ListApprovalInboxAsync(
                fixture.TenantId, otherLegalEntityId, It.IsAny<IReadOnlyCollection<Guid>>(),
                null, null, 0, 20, It.IsAny<CancellationToken>()))
            .ReturnsAsync((Array.Empty<WorkAreaChangeRequest>(), 0));
        fixture.Attendance.Setup(x => x.ListEmployeeIdentitiesAsync(
                fixture.TenantId, otherLegalEntityId, It.IsAny<IReadOnlyCollection<Guid>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Dictionary<Guid, AttendanceHistoryEmployee>());

        var result = await fixture.Workflow.ListApprovalsAsync(
            new ListWorkAreaChangeRequestApprovalsQuery(null, null, new PagedRequest()), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        fixture.Employees.Verify(x => x.GetDefaultForUserAsync(
            It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    private sealed class Fixture
    {
        public Guid TenantId { get; } = Guid.NewGuid();
        public Guid RequesterUserId { get; } = Guid.NewGuid();
        public Guid ApproverUserId { get; } = Guid.NewGuid();
        public Guid EmployeeId { get; } = Guid.NewGuid();
        public Guid ApproverEmployeeId { get; } = Guid.NewGuid();
        public Guid LegalEntityId { get; } = Guid.NewGuid();
        public Guid PositionId { get; } = Guid.NewGuid();
        public DateOnly Date { get; } = new(2026, 8, 25);
        public DateTimeOffset Now { get; } = new(2026, 8, 25, 8, 0, 0, TimeSpan.Zero);
        public int OriginalWorkModeId { get; } = 77;
        public Employee Employee { get; }
        public Mock<ICurrentUser> CurrentUser { get; } = new();
        public Mock<IDateTimeProvider> Clock { get; } = new();
        public Mock<CoreEmployeeRepository> Employees { get; } = new();
        public Mock<ISessionRepository> Sessions { get; } = new();
        public Mock<ILegalEntityRepository> LegalEntities { get; } = new();
        public Mock<IWorkAreaChangeRequestRepository> Requests { get; } = new();
        public Mock<IAttendanceReadRepository> Attendance { get; } = new();
        public Mock<IExpectedWorkAreaResolver> ExpectedAreas { get; } = new();
        public Mock<IEmployeeAuthorityResolver> Authority { get; } = new();
        public Mock<IPositionRepository> Positions { get; } = new();
        public Mock<INotificationDispatcher> Dispatcher { get; } = new();
        public FakeUnitOfWork UnitOfWork { get; } = new();
        public WorkAreaChangeRequest? AddedRequest { get; private set; }
        public List<NotificationCall> Notifications { get; } = [];
        public bool NotificationWasCreatedInsideTransaction { get; private set; }
        public WorkAreaChangeRequestWorkflow Workflow { get; }

        public Fixture(bool actingAsApprover = false)
        {
            var actorUserId = actingAsApprover ? ApproverUserId : RequesterUserId;
            CurrentUser.SetupGet(x => x.IsAuthenticated).Returns(true);
            CurrentUser.SetupGet(x => x.UserId).Returns(actorUserId);
            CurrentUser.SetupGet(x => x.TenantId).Returns(TenantId);
            CurrentUser.SetupGet(x => x.SessionId).Returns((Guid?)null);
            CurrentUser.Setup(x => x.HasPermission(It.IsAny<string>())).Returns(true);
            Clock.SetupGet(x => x.UtcNow).Returns(Now);

            Employee = new Employee
            {
                Id = EmployeeId, TenantId = TenantId, UserId = RequesterUserId,
                LegalEntityId = LegalEntityId, FirstName = "Alex", LastName = "Employee",
                WorkModeId = OriginalWorkModeId
            };
            var approver = new Employee
            {
                Id = ApproverEmployeeId, TenantId = TenantId, UserId = ApproverUserId,
                LegalEntityId = LegalEntityId, FirstName = "Sam", LastName = "Approver"
            };
            var legalEntity = new LegalEntity
            {
                Id = LegalEntityId, TenantId = TenantId, Name = "Acme", Timezone = "UTC",
                WorkStartTime = new TimeOnly(9), WorkEndTime = new TimeOnly(17),
                StandardWorkingDays = "[1,2,3,4,5]"
            };

            Employees.Setup(x => x.GetDefaultForUserAsync(TenantId, It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(Employee);
            Employees.Setup(x => x.GetByIdAsync(TenantId, EmployeeId, It.IsAny<CancellationToken>()))
                .ReturnsAsync(Employee);
            Employees.Setup(x => x.GetByIdAsync(TenantId, ApproverEmployeeId, It.IsAny<CancellationToken>()))
                .ReturnsAsync(approver);
            LegalEntities.Setup(x => x.GetByIdForTenantAsync(TenantId, LegalEntityId, It.IsAny<CancellationToken>()))
                .ReturnsAsync(legalEntity);
            Requests.Setup(x => x.HasActiveForDateAsync(TenantId, EmployeeId, Date, It.IsAny<CancellationToken>()))
                .ReturnsAsync(false);
            Requests.Setup(x => x.AddAsync(It.IsAny<WorkAreaChangeRequest>(), It.IsAny<CancellationToken>()))
                .Callback<WorkAreaChangeRequest, CancellationToken>((request, _) => AddedRequest = request)
                .Returns(Task.CompletedTask);
            Requests.Setup(x => x.SaveChangesAsync(It.IsAny<CancellationToken>())).ReturnsAsync(1);
            Positions.Setup(x => x.GetByIdAsync(TenantId, PositionId, It.IsAny<CancellationToken>()))
                .ReturnsAsync(new Position { Id = PositionId, TenantId = TenantId, LegalEntityId = LegalEntityId, Name = "Manager" });
            ExpectedAreas.Setup(x => x.ResolveAsync(
                    It.IsAny<Employee>(), It.IsAny<LegalEntity>(), It.IsAny<DateOnly>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(Result<ExpectedWorkAreaResolution>.Success(new("onsite", "UTC", "active_employee_work_mode")));
            Authority.Setup(x => x.ResolveApproverAsync(
                    It.IsAny<EmployeeApprovalRouteRequest>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(Result<EmployeeApprovalRoute>.Success(new(
                    ApproverEmployeeId, ApproverUserId, PositionId, "attendance:approve",
                    EmployeeAuthorityPurpose.WorkAreaChangeApproval,
                    EmployeeApprovalRouteSource.PositionCoverage, 1)));
            Dispatcher.Setup(x => x.SendTemplatedAsync(
                    It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<string>(),
                    It.IsAny<IReadOnlyDictionary<string, string>>(), It.IsAny<string?>(), It.IsAny<Guid?>(),
                    It.IsAny<CancellationToken>()))
                .Callback((Guid tenantId, Guid userId, string code, IReadOnlyDictionary<string, string> _,
                    string? relatedType, Guid? relatedId, CancellationToken _) =>
                {
                    NotificationWasCreatedInsideTransaction |= UnitOfWork.IsInTransaction;
                    Notifications.Add(new NotificationCall(tenantId, userId, code, relatedType, relatedId));
                })
                .Returns(Task.CompletedTask);

            Workflow = new WorkAreaChangeRequestWorkflow(
                CurrentUser.Object, Clock.Object, Employees.Object, Sessions.Object, LegalEntities.Object,
                Requests.Object, Attendance.Object, ExpectedAreas.Object, Authority.Object,
                Positions.Object, Dispatcher.Object, UnitOfWork);
        }

        public WorkAreaChangeRequest PendingRequest() => new()
        {
            Id = Guid.NewGuid(), TenantId = TenantId, EmployeeId = EmployeeId, LegalEntityId = LegalEntityId,
            Date = Date, CurrentExpectedWorkArea = "onsite", RequestedWorkArea = "remote", Reason = "Reason",
            Status = WorkAreaChangeRequest.StatusPending, RequestedAt = Now
        };
    }

    private sealed record NotificationCall(
        Guid TenantId, Guid RecipientUserId, string TemplateCode,
        string? RelatedEntityType, Guid? RelatedEntityId);
}
