using Moq;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.CoreHr.EmployeeAuthority.Models;
using ONEVO.Application.Features.CoreHr.EmployeeAuthority.ServiceInterfaces;
using ONEVO.Application.Features.Dashboard.Team.DTOs;
using ONEVO.Application.Features.Dashboard.Team.Sources;
using ONEVO.Application.Features.TimeAttendance.DTOs.Responses;
using ONEVO.Application.Features.TimeAttendance.RepositoryInterfaces;
using ONEVO.Domain.Features.TimeAttendance.Entities;
using Xunit;

namespace ONEVO.Tests.Unit.Features.Dashboard.Team.Sources;

public sealed class AttendanceWorkAreaChangeTeamActionSourceTests
{
    private static readonly Guid TenantId = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
    private static readonly Guid LegalEntityId = Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb");

    private static WorkAreaChangeRequest Request(Guid employeeId, DateTimeOffset requestedAt) => new()
    {
        Id = Guid.NewGuid(), TenantId = TenantId, EmployeeId = employeeId, LegalEntityId = LegalEntityId,
        Date = DateOnly.FromDateTime(requestedAt.UtcDateTime), Status = WorkAreaChangeRequest.StatusPending,
        RequestedAt = requestedAt,
    };

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task IsGatedAsync_reflects_attendance_approve_permission(bool hasPermission)
    {
        var currentUser = new Mock<ICurrentUser>();
        currentUser.Setup(x => x.HasPermission("attendance:approve")).Returns(hasPermission);
        var source = new AttendanceWorkAreaChangeTeamActionSource(
            currentUser.Object, Mock.Of<IEmployeeAuthorityResolver>(),
            Mock.Of<IWorkAreaChangeRequestRepository>(), Mock.Of<IAttendanceReadRepository>());

        Assert.Equal(hasPermission, await source.IsGatedAsync(CancellationToken.None));
    }

    [Fact]
    public async Task Goes_through_the_two_step_gate_and_uses_RequestedAt_for_ordering()
    {
        var currentUser = new Mock<ICurrentUser>();
        currentUser.SetupGet(x => x.TenantId).Returns(TenantId);
        var employeeId = Guid.NewGuid();
        var candidateIds = new[] { employeeId };

        var requests = new Mock<IWorkAreaChangeRequestRepository>();
        requests.Setup(x => x.ListPendingEmployeeIdsAsync(TenantId, LegalEntityId, null, null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(candidateIds);

        var authority = new Mock<IEmployeeAuthorityResolver>();
        authority.Setup(x => x.ResolveApprovalInboxScopeAsync(
                It.Is<EmployeeApprovalInboxScopeRequest>(r =>
                    r.LegalEntityId == LegalEntityId && r.RequiredPermission == "attendance:approve"
                    && r.Purpose == EmployeeAuthorityPurpose.WorkAreaChangeApproval
                    && r.CandidateEmployeeIds.SequenceEqual(candidateIds)),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(candidateIds);

        var older = Request(employeeId, DateTimeOffset.Parse("2026-08-01T00:00:00Z"));
        var newer = Request(employeeId, DateTimeOffset.Parse("2026-09-01T00:00:00Z"));
        requests.Setup(x => x.ListApprovalInboxAsync(
                TenantId, LegalEntityId, It.Is<IReadOnlyCollection<Guid>>(ids => ids.SequenceEqual(candidateIds)),
                null, null, 0, int.MaxValue, It.IsAny<CancellationToken>()))
            .ReturnsAsync((new List<WorkAreaChangeRequest> { newer, older }, 2));

        var attendance = new Mock<IAttendanceReadRepository>();
        attendance.Setup(x => x.ListEmployeeIdentitiesAsync(TenantId, LegalEntityId, It.IsAny<IReadOnlyCollection<Guid>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Dictionary<Guid, AttendanceHistoryEmployee> { [employeeId] = new(employeeId, "Arjun M", "E1", null, null, null) });

        var source = new AttendanceWorkAreaChangeTeamActionSource(currentUser.Object, authority.Object, requests.Object, attendance.Object);
        var summary = await source.GetSummaryAsync(LegalEntityId, 5, CancellationToken.None);

        Assert.Equal(2, summary.PendingCount);
        Assert.Equal(older.RequestedAt, summary.OldestPendingAt);
        Assert.Equal(older.Id, summary.TopItems[0].EntityId);
        Assert.Equal("work-area", summary.TopItems[0].Link.Params["type"]);
    }

    [Fact]
    public async Task Empty_result_skips_identity_resolution()
    {
        var currentUser = new Mock<ICurrentUser>();
        currentUser.SetupGet(x => x.TenantId).Returns(TenantId);
        var requests = new Mock<IWorkAreaChangeRequestRepository>();
        requests.Setup(x => x.ListPendingEmployeeIdsAsync(TenantId, LegalEntityId, null, null, It.IsAny<CancellationToken>()))
            .ReturnsAsync([]);
        var authority = new Mock<IEmployeeAuthorityResolver>();
        authority.Setup(x => x.ResolveApprovalInboxScopeAsync(It.IsAny<EmployeeApprovalInboxScopeRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([]);
        requests.Setup(x => x.ListApprovalInboxAsync(
                TenantId, LegalEntityId, It.IsAny<IReadOnlyCollection<Guid>>(), null, null, 0, int.MaxValue, It.IsAny<CancellationToken>()))
            .ReturnsAsync((new List<WorkAreaChangeRequest>(), 0));
        var attendance = new Mock<IAttendanceReadRepository>();

        var source = new AttendanceWorkAreaChangeTeamActionSource(currentUser.Object, authority.Object, requests.Object, attendance.Object);
        var summary = await source.GetSummaryAsync(LegalEntityId, 5, CancellationToken.None);

        Assert.Equal(0, summary.PendingCount);
        attendance.Verify(x => x.ListEmployeeIdentitiesAsync(
            It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<IReadOnlyCollection<Guid>>(), It.IsAny<CancellationToken>()), Times.Never);
    }
}
