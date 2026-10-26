using FluentAssertions;
using Moq;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Common.RepositoryInterfaces;
using ONEVO.Application.Features.Auth.Permission.RepositoryInterfaces;
using ONEVO.Application.Features.Conversations.Contracts;
using ONEVO.Application.Features.CoreHr.EmployeeAuthority.Models;
using ONEVO.Application.Features.CoreHr.EmployeeAuthority.ServiceInterfaces;
using ONEVO.Application.Features.CoreHr.EmployeeAuthority.Services;
using ONEVO.Application.Features.Leave.Approval.RepositoryInterfaces;
using ONEVO.Application.Features.Leave.Inbox;
using ONEVO.Application.Features.Monitoring.TrayActivation.RepositoryInterfaces;
using ONEVO.Application.Features.TimeAttendance.Alerts;
using ONEVO.Application.Features.TimeAttendance.Inbox;
using ONEVO.Application.Features.TimeAttendance.RepositoryInterfaces;
using ONEVO.Domain.Features.CoreHr.Entities;
using ONEVO.Domain.Features.Leave.Request.Entities;
using ONEVO.Domain.Features.TimeAttendance.Alerts;
using ONEVO.Domain.Features.TimeAttendance.Entities;
using ONEVO.Tests.Unit.Fakes;
using Xunit;

namespace ONEVO.Tests.Unit.Features.Inbox;

/// <summary>Plan BZ coverage audit: who is in the chat of a leave / attendance item, and who may view or export it.</summary>
public sealed class HrConversationResolverCoverageTests
{
    private static readonly Guid TenantId = Guid.NewGuid();
    private static readonly DateTimeOffset Now = new(2026, 10, 14, 10, 0, 0, TimeSpan.Zero);
    private readonly Employee _requester = new() { Id = Guid.NewGuid(), UserId = Guid.NewGuid(), FirstName = "Pat", LastName = "Doe", LegalEntityId = Guid.NewGuid() };
    private readonly Employee _hr = new() { Id = Guid.NewGuid(), UserId = Guid.NewGuid() };
    private readonly Employee _other = new() { Id = Guid.NewGuid(), UserId = Guid.NewGuid() };
    private readonly Mock<IEmployeeRepository> _employees = new();
    private readonly Mock<IPermissionRepository> _permissions = new();

    public HrConversationResolverCoverageTests()
    {
        foreach (var e in new[] { _requester, _hr, _other })
            _employees.Setup(x => x.GetByIdAsync(TenantId, e.Id, It.IsAny<CancellationToken>())).ReturnsAsync(e);
        _permissions.Setup(p => p.ListUserIdsWithPermissionCodeAsync(TenantId, "leave:read", Now, It.IsAny<CancellationToken>())).ReturnsAsync([_hr.UserId]);
        _permissions.Setup(p => p.ListUserIdsWithPermissionCodeAsync(TenantId, "attendance:read", Now, It.IsAny<CancellationToken>())).ReturnsAsync([_hr.UserId]);
    }

    // ---- leave ----------------------------------------------------------------------------------------------------------

    private LeaveConversationSubjectResolver Leave(Mock<ILeaveApprovalRepository> approvals)
        => new(approvals.Object, _employees.Object, _permissions.Object, new FakeDateTimeProvider { UtcNow = Now });

    [Fact]
    public async Task A_leave_chat_has_the_employee_the_on_behalf_submitter_every_approver_and_a_delegates_original_approver()
    {
        var submitter = new Employee { Id = Guid.NewGuid(), UserId = Guid.NewGuid() };
        _employees.Setup(x => x.GetByUserIdAsync(TenantId, submitter.UserId, It.IsAny<CancellationToken>())).ReturnsAsync(submitter);
        Guid approver = Guid.NewGuid(), delegateApprover = Guid.NewGuid(), original = Guid.NewGuid();
        var request = new LeaveRequest { Id = Guid.NewGuid(), TenantId = TenantId, EmployeeId = _requester.Id, SubmittedOnBehalfOfBy = submitter.UserId };
        var approvals = new Mock<ILeaveApprovalRepository>();
        approvals.Setup(a => a.GetStateAsync(TenantId, request.Id, It.IsAny<CancellationToken>())).ReturnsAsync(new LeaveApprovalState(
            request, null, _requester, "Annual leave", "AL", null,
            [
                new LeaveRequestApprover { ApproverEmployeeId = approver },
                new LeaveRequestApprover { ApproverEmployeeId = delegateApprover, DelegatedFromApproverId = original },
            ]));

        var subject = await Leave(approvals).ResolveAsync(TenantId, new ItemRef("leave", "request", request.Id), default);

        subject!.BaseParticipantEmployeeIds.Should().Equal(_requester.Id, submitter.Id, approver, delegateApprover, original);
        subject.Title.Should().Be("Annual leave - Pat Doe");
    }

    [Fact]
    public async Task An_unknown_leave_kind_or_a_missing_request_has_no_chat_and_only_leave_readers_may_view_or_export()
    {
        var resolver = Leave(new Mock<ILeaveApprovalRepository>());
        var item = new ItemRef("leave", "request", Guid.NewGuid());

        (await resolver.ResolveAsync(TenantId, item with { Kind = "other" }, default)).Should().BeNull();
        (await resolver.ResolveAsync(TenantId, item, default)).Should().BeNull();
        (await resolver.CanViewAsync(TenantId, item, _hr.Id, default)).Should().BeTrue();
        (await resolver.CanReviewAsync(TenantId, item, _hr.Id, default)).Should().BeTrue();
        (await resolver.CanViewAsync(TenantId, item, _other.Id, default)).Should().BeFalse();
        (await resolver.CanViewAsync(TenantId, item, Guid.NewGuid(), default)).Should().BeFalse("an unknown employee");
    }

    // ---- attendance ---------------------------------------------------------------------------------------------------

    private readonly Mock<IAttendanceCorrectionRepository> _corrections = new();
    private readonly Mock<IAttendanceAlertRepository> _alerts = new();
    private readonly Mock<IEmployeeAuthorityResolver> _authority = new();

    private AttendanceConversationSubjectResolver Attendance()
    {
        var factory = new Mock<IEmployeeAuthorityResolverFactory>();
        factory.Setup(f => f.CreateForTenant(TenantId)).Returns(_authority.Object);
        return new AttendanceConversationSubjectResolver(_corrections.Object, Mock.Of<IWorkAreaChangeRequestRepository>(),
            Mock.Of<ILocationChangeRequestRepository>(), Mock.Of<IDeviceChangeRequestRepository>(), _employees.Object, factory.Object,
            _permissions.Object, new FakeDateTimeProvider { UtcNow = Now }, _alerts.Object);
    }

    private void ApproverIs(Guid approverEmployeeId)
        => _authority.Setup(a => a.ResolveApproverAsync(It.IsAny<EmployeeApprovalRouteRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<EmployeeApprovalRoute>.Success(new EmployeeApprovalRoute(approverEmployeeId, Guid.NewGuid(), Guid.NewGuid(),
                "attendance:approve", EmployeeAuthorityPurpose.AttendanceCorrectionApproval, EmployeeApprovalRouteSource.PositionCoverage, 1)));

    [Fact]
    public async Task An_attendance_correction_chat_is_the_requester_and_the_approver_resolved_now()
    {
        var approver = Guid.NewGuid();
        ApproverIs(approver);
        var correction = new AttendanceCorrection { Id = Guid.NewGuid(), TenantId = TenantId, EmployeeId = _requester.Id, LegalEntityId = Guid.NewGuid(), WorkDate = new DateOnly(2026, 10, 13) };
        _corrections.Setup(c => c.GetByIdAsync(TenantId, correction.Id, It.IsAny<CancellationToken>())).ReturnsAsync(correction);

        var subject = await Attendance().ResolveAsync(TenantId, new ItemRef("attendance", "correction", correction.Id), default);

        subject!.BaseParticipantEmployeeIds.Should().Equal(_requester.Id, approver);
        subject.Title.Should().Be("Attendance correction · 13 Oct 2026");
        _authority.Verify(a => a.ResolveApproverAsync(It.Is<EmployeeApprovalRouteRequest>(r => r.LegalEntityId == correction.LegalEntityId), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task An_alert_without_its_own_legal_entity_routes_through_the_employees_and_a_failed_route_leaves_only_the_employee()
    {
        _authority.Setup(a => a.ResolveApproverAsync(It.IsAny<EmployeeApprovalRouteRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<EmployeeApprovalRoute>.UnprocessableEntity("No eligible approver."));
        var alert = new AttendanceAlert { Id = Guid.NewGuid(), TenantId = TenantId, EmployeeId = _requester.Id, SignalKey = AttendanceAlertSignals.LateClockIns, WorkDate = new DateOnly(2026, 10, 13) };
        _alerts.Setup(a => a.GetByIdAsync(TenantId, alert.Id, It.IsAny<CancellationToken>())).ReturnsAsync(alert);

        var subject = await Attendance().ResolveAsync(TenantId, new ItemRef("attendance", "alert", alert.Id), default);

        subject!.BaseParticipantEmployeeIds.Should().Equal(_requester.Id);
        subject.Title.Should().Be("Late clock-in · 13 Oct 2026");
        _authority.Verify(a => a.ResolveApproverAsync(It.Is<EmployeeApprovalRouteRequest>(r => r.LegalEntityId == _requester.LegalEntityId), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task An_unknown_or_missing_attendance_item_has_no_chat_and_only_attendance_readers_may_view_or_export()
    {
        var resolver = Attendance();
        var item = new ItemRef("attendance", "correction", Guid.NewGuid());

        (await resolver.ResolveAsync(TenantId, item, default)).Should().BeNull();
        (await resolver.ResolveAsync(TenantId, item with { Kind = "other" }, default)).Should().BeNull();
        (await resolver.CanViewAsync(TenantId, item, _hr.Id, default)).Should().BeFalse("a missing item has no employee in anyone's scope");
        (await resolver.CanReviewAsync(TenantId, item, _other.Id, default)).Should().BeFalse();
    }

    // ---- final review fixes -----------------------------------------------------------------------

    private void ReaderCovers(params Guid[] employeeIds)
        => _authority.Setup(a => a.ResolveVisibilityAsync(It.IsAny<EmployeeAuthorityVisibilityRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new EmployeeAuthorityVisibilityScope(_hr.UserId, Guid.Empty, false, employeeIds));

    [Fact]
    public async Task Attendance_readers_view_and_export_only_items_of_employees_in_their_coverage()
    {
        var correction = new AttendanceCorrection { Id = Guid.NewGuid(), TenantId = TenantId, EmployeeId = _requester.Id, LegalEntityId = Guid.NewGuid(), WorkDate = new DateOnly(2026, 10, 13) };
        _corrections.Setup(c => c.GetByIdAsync(TenantId, correction.Id, It.IsAny<CancellationToken>())).ReturnsAsync(correction);
        var item = new ItemRef("attendance", "correction", correction.Id);
        var resolver = Attendance();

        ReaderCovers();
        (await resolver.CanViewAsync(TenantId, item, _hr.Id, default)).Should().BeFalse();
        (await resolver.CanReviewAsync(TenantId, item, _hr.Id, default)).Should().BeFalse();

        ReaderCovers(_requester.Id);
        (await resolver.CanViewAsync(TenantId, item, _hr.Id, default)).Should().BeTrue();
        (await resolver.CanReviewAsync(TenantId, item, _hr.Id, default)).Should().BeTrue();
        (await resolver.CanViewAsync(TenantId, item, _other.Id, default)).Should().BeFalse("no attendance:read");
        _authority.Verify(a => a.ResolveVisibilityAsync(It.Is<EmployeeAuthorityVisibilityRequest>(r =>
            r.ActorUserId == _hr.UserId && r.LegalEntityId == correction.LegalEntityId && r.RequiredPermission == "attendance:read"), It.IsAny<CancellationToken>()), Times.AtLeastOnce);
    }

    [Fact]
    public async Task An_alert_chat_names_the_employee_as_its_subject_so_they_can_post_a_justification()
    {
        ApproverIs(Guid.NewGuid());
        var alert = new AttendanceAlert { Id = Guid.NewGuid(), TenantId = TenantId, EmployeeId = _requester.Id, LegalEntityId = Guid.NewGuid(), SignalKey = AttendanceAlertSignals.AbsentDays, WorkDate = new DateOnly(2026, 10, 13) };
        _alerts.Setup(a => a.GetByIdAsync(TenantId, alert.Id, It.IsAny<CancellationToken>())).ReturnsAsync(alert);
        var correction = new AttendanceCorrection { Id = Guid.NewGuid(), TenantId = TenantId, EmployeeId = _requester.Id, LegalEntityId = Guid.NewGuid(), WorkDate = new DateOnly(2026, 10, 13) };
        _corrections.Setup(c => c.GetByIdAsync(TenantId, correction.Id, It.IsAny<CancellationToken>())).ReturnsAsync(correction);

        (await Attendance().ResolveAsync(TenantId, new ItemRef("attendance", "alert", alert.Id), default))!.SubjectEmployeeId.Should().Be(_requester.Id);
        (await Attendance().ResolveAsync(TenantId, new ItemRef("attendance", "correction", correction.Id), default))!.SubjectEmployeeId
            .Should().BeNull("a request has a requester, not an investigated subject");
    }
}
