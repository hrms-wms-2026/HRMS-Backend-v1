using Moq;
using ONEVO.Api.Controllers.Tenant.Attendance;
using ONEVO.Api.Dashboard.Widgets;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Features.TimeAttendance.Queries;
using ONEVO.Application.Features.TimeAttendance.Team.DTOs;
using Xunit;

namespace ONEVO.Tests.Unit.Api.Dashboard.Widgets;

public sealed class TeamStatusWidgetDataProviderTests
{
    private static TeamStatusWidgetDataProvider Create(WidgetProviderTestKit kit) =>
        new(kit.Mediator.Object, kit.User.Object, kit.Entitlements.Object);

    [Fact]
    public async Task Missing_permission_is_forbidden()
    {
        // TimeTrackingController.TeamToday is gated by [RequirePermission("attendance:read")].
        var kit = new WidgetProviderTestKit(permissions: ["attendance:read:self"], activeModules: WidgetProviderTestKit.WorkModules);

        var result = await Create(kit).GetAsync(default);

        WidgetProviderTestKit.AssertForbidden(result);
        kit.Mediator.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task Returns_same_view_model_as_endpoint()
    {
        var kit = new WidgetProviderTestKit(permissions: ["attendance:read"]);
        var response = new TeamTodayResponse(
            new DateOnly(2026, 10, 20), "Asia/Colombo", Guid.NewGuid(),
            new TeamTodaySummary(1, 1, 0, 0, 0, 0, 0, 0, 0, 0),
            [new TeamTodayMember(Guid.NewGuid(), "Ada Lovelace", null, "Engineer", "working", "Working", false,
                new DateTimeOffset(2026, 10, 20, 3, 30, 0, TimeSpan.Zero), null, null, null, null, null)],
            1);
        kit.Mediator.Setup(m => m.Send(It.IsAny<GetCoveredTeamTodayQuery>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<TeamTodayResponse>.Success(response));

        var provided = await Create(kit).GetAsync(default);
        // MyTeamTodayApiService.getTeamToday() sends limit=50 (store passes nothing).
        var endpoint = await new TimeTrackingController(kit.Mediator.Object).TeamToday(50, default);

        WidgetProviderTestKit.AssertSameBody(endpoint, provided);
        kit.Mediator.Verify(m => m.Send(It.Is<GetCoveredTeamTodayQuery>(q => q.Limit == 50), It.IsAny<CancellationToken>()),
            Times.Exactly(2));
    }
}
