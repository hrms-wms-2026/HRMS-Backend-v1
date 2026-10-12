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

public sealed class AttendanceCorrectionTeamActionSourceTests
{
    private static readonly Guid TenantId = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
    private static readonly Guid UserId = Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb");
    private static readonly Guid LegalEntityId = Guid.Parse("cccccccc-cccc-cccc-cccc-cccccccccccc");

    private static AttendanceCorrection Correction(Guid employeeId, DateTimeOffset createdAt) => new()
    {
        Id = Guid.NewGuid(), TenantId = TenantId, EmployeeId = employeeId, LegalEntityId = LegalEntityId,
        WorkDate = DateOnly.FromDateTime(createdAt.UtcDateTime), CorrectionType = AttendanceCorrection.TypeClockIn,
        Status = AttendanceCorrection.StatusPending, CreatedAt = createdAt,
    };

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task IsGatedAsync_reflects_attendance_approve_permission(bool hasPermission)
    {
        var currentUser = new Mock<ICurrentUser>();
        currentUser.Setup(x => x.HasPermission("attendance:approve")).Returns(hasPermission);
        var source = new AttendanceCorrectionTeamActionSource(
            currentUser.Object, Mock.Of<IEmployeeAuthorityResolver>(),
            Mock.Of<IAttendanceCorrectionRepository>(), Mock.Of<IAttendanceReadRepository>());

        Assert.Equal(hasPermission, await source.IsGatedAsync(CancellationToken.None));
    }

    [Fact]
    public async Task Empty_inbox_returns_zero_summary_without_resolving_identities()
    {
        var currentUser = CurrentUser();
        var authority = AuthorityVisible();
        var corrections = new Mock<IAttendanceCorrectionRepository>();
        corrections.Setup(x => x.ListApprovalInboxAsync(TenantId, LegalEntityId, It.IsAny<IReadOnlyCollection<Guid>>(), null, null, AttendanceCorrection.StatusPending, It.IsAny<CancellationToken>()))
            .ReturnsAsync([]);
        var attendance = new Mock<IAttendanceReadRepository>();
        var source = new AttendanceCorrectionTeamActionSource(currentUser, authority, corrections.Object, attendance.Object);

        var summary = await source.GetSummaryAsync(LegalEntityId, 5, CancellationToken.None);

        Assert.Equal(0, summary.PendingCount);
        Assert.Empty(summary.TopItems);
        attendance.Verify(x => x.ListEmployeeIdentitiesAsync(
            It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<IReadOnlyCollection<Guid>>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Orders_oldest_first_resolves_identities_and_builds_the_deep_link()
    {
        var currentUser = CurrentUser();
        var employeeId = Guid.NewGuid();
        var authority = AuthorityVisible(employeeId);
        var older = Correction(employeeId, DateTimeOffset.Parse("2026-08-01T00:00:00Z"));
        var newer = Correction(employeeId, DateTimeOffset.Parse("2026-09-01T00:00:00Z"));
        var corrections = new Mock<IAttendanceCorrectionRepository>();
        corrections.Setup(x => x.ListApprovalInboxAsync(TenantId, LegalEntityId, It.IsAny<IReadOnlyCollection<Guid>>(), null, null, AttendanceCorrection.StatusPending, It.IsAny<CancellationToken>()))
            .ReturnsAsync([newer, older]);
        var attendance = new Mock<IAttendanceReadRepository>();
        attendance.Setup(x => x.ListEmployeeIdentitiesAsync(TenantId, LegalEntityId, It.IsAny<IReadOnlyCollection<Guid>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Dictionary<Guid, AttendanceHistoryEmployee> { [employeeId] = new(employeeId, "Priya K", "E1", null, null, null) });

        var source = new AttendanceCorrectionTeamActionSource(currentUser, authority, corrections.Object, attendance.Object);
        var summary = await source.GetSummaryAsync(LegalEntityId, 5, CancellationToken.None);

        Assert.Equal(2, summary.PendingCount);
        Assert.Equal(older.CreatedAt, summary.OldestPendingAt);
        Assert.Equal(older.Id, summary.TopItems[0].EntityId);
        Assert.Equal("Priya K", summary.TopItems[0].SubjectName);
        Assert.Equal(ActionItemLink.KindAttendanceApproval, summary.TopItems[0].Link.Kind);
        Assert.Equal("corrections", summary.TopItems[0].Link.Params["type"]);
        Assert.Equal(older.Id.ToString(), summary.TopItems[0].Link.Params["requestId"]);
    }

    private static ICurrentUser CurrentUser()
    {
        var currentUser = new Mock<ICurrentUser>();
        currentUser.SetupGet(x => x.TenantId).Returns(TenantId);
        currentUser.SetupGet(x => x.UserId).Returns(UserId);
        currentUser.Setup(x => x.HasPermission("attendance:approve")).Returns(true);
        return currentUser.Object;
    }

    private static IEmployeeAuthorityResolver AuthorityVisible(params Guid[] employeeIds)
    {
        var authority = new Mock<IEmployeeAuthorityResolver>();
        authority.Setup(x => x.ResolveVisibilityAsync(It.IsAny<EmployeeAuthorityVisibilityRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new EmployeeAuthorityVisibilityScope(UserId, LegalEntityId, false, employeeIds));
        return authority.Object;
    }
}
