using Moq;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.CoreHr.EmployeeAuthority.Models;
using ONEVO.Application.Features.CoreHr.EmployeeAuthority.ServiceInterfaces;
using ONEVO.Application.Features.Dashboard.Team.DTOs;
using ONEVO.Application.Features.Dashboard.Team.Sources;
using ONEVO.Application.Features.Monitoring.TrayActivation.RepositoryInterfaces;
using ONEVO.Application.Features.TimeAttendance.DTOs.Responses;
using ONEVO.Application.Features.TimeAttendance.RepositoryInterfaces;
using ONEVO.Domain.Features.Monitoring.TrayActivation.Entities;
using Xunit;

namespace ONEVO.Tests.Unit.Features.Dashboard.Team.Sources;

public sealed class AttendanceDeviceChangeTeamActionSourceTests
{
    private static readonly Guid TenantId = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
    private static readonly Guid LegalEntityId = Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb");

    private static DeviceChangeRequest Request(Guid employeeId, DateTimeOffset requestedAt) => new()
    {
        Id = Guid.NewGuid(), TenantId = TenantId, EmployeeId = employeeId, LegalEntityId = LegalEntityId,
        NewDeviceFingerprint = "abc", NewDeviceName = "Laptop", NewDeviceOs = "Windows",
        Status = DeviceChangeRequest.StatusPending, RequestedAt = requestedAt,
    };

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task IsGatedAsync_reflects_attendance_approve_permission(bool hasPermission)
    {
        var currentUser = new Mock<ICurrentUser>();
        currentUser.Setup(x => x.HasPermission("attendance:approve")).Returns(hasPermission);
        var source = new AttendanceDeviceChangeTeamActionSource(
            currentUser.Object, Mock.Of<IEmployeeAuthorityResolver>(),
            Mock.Of<IDeviceChangeRequestRepository>(), Mock.Of<IAttendanceReadRepository>());

        Assert.Equal(hasPermission, await source.IsGatedAsync(CancellationToken.None));
    }

    [Fact]
    public async Task Goes_through_the_two_step_gate_and_uses_RequestedAt_for_ordering()
    {
        var currentUser = new Mock<ICurrentUser>();
        currentUser.SetupGet(x => x.TenantId).Returns(TenantId);
        var employeeId = Guid.NewGuid();
        var candidateIds = new[] { employeeId };

        var requests = new Mock<IDeviceChangeRequestRepository>();
        requests.Setup(x => x.ListPendingEmployeeIdsAsync(TenantId, LegalEntityId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(candidateIds);

        var authority = new Mock<IEmployeeAuthorityResolver>();
        authority.Setup(x => x.ResolveApprovalInboxScopeAsync(
                It.Is<EmployeeApprovalInboxScopeRequest>(r => r.Purpose == EmployeeAuthorityPurpose.DeviceChangeApproval),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(candidateIds);

        var older = Request(employeeId, DateTimeOffset.Parse("2026-08-01T00:00:00Z"));
        var newer = Request(employeeId, DateTimeOffset.Parse("2026-09-01T00:00:00Z"));
        requests.Setup(x => x.ListApprovalInboxAsync(
                TenantId, LegalEntityId, It.Is<IReadOnlyCollection<Guid>>(ids => ids.SequenceEqual(candidateIds)),
                0, int.MaxValue, It.IsAny<CancellationToken>()))
            .ReturnsAsync((new List<DeviceChangeRequest> { newer, older }, 2));

        var attendance = new Mock<IAttendanceReadRepository>();
        attendance.Setup(x => x.ListEmployeeIdentitiesAsync(TenantId, LegalEntityId, It.IsAny<IReadOnlyCollection<Guid>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Dictionary<Guid, AttendanceHistoryEmployee> { [employeeId] = new(employeeId, "Arjun M", "E1", null, null, null) });

        var source = new AttendanceDeviceChangeTeamActionSource(currentUser.Object, authority.Object, requests.Object, attendance.Object);
        var summary = await source.GetSummaryAsync(LegalEntityId, 5, CancellationToken.None);

        Assert.Equal(2, summary.PendingCount);
        Assert.Equal(older.RequestedAt, summary.OldestPendingAt);
        Assert.Equal(older.Id, summary.TopItems[0].EntityId);
        Assert.Equal("device-change", summary.TopItems[0].Link.Params["type"]);
    }
}
