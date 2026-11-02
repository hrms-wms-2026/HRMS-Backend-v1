using Moq;
using ONEVO.Api.Controllers.Tenant.WorkManagement;
using ONEVO.Api.Dashboard.Widgets;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Features.WorkManagement.Planner.TeamSchedule;
using Xunit;

namespace ONEVO.Tests.Unit.Api.Dashboard.Widgets;

public sealed class TeamScheduleWidgetDataProviderTests
{
    private static TeamScheduleWidgetDataProvider Create(WidgetProviderTestKit kit) =>
        new(kit.Mediator.Object, kit.User.Object, kit.Entitlements.Object);

    [Fact]
    public async Task Returns_same_view_model_as_endpoint()
    {
        var kit = new WidgetProviderTestKit(permissions: [], activeModules: WidgetProviderTestKit.WorkModules);
        var response = new TeamScheduleResponse(new DateOnly(2026, 11, 2), false, new DateTimeOffset(2026, 11, 2, 6, 0, 0, TimeSpan.Zero),
            new TeamScheduleSummary(1, 0, 0, 0, 0, 1, 0, 0, 0),
            [new TeamScheduleMember(Guid.NewGuid(), "Ada Lovelace", null, "Asia/Colombo", "working", 0, 30, 45, 0, 0, 0,
                null, null, null, "planner_off", null, [], [], [], 480, [])]);
        kit.Mediator.Setup(m => m.Send(It.IsAny<GetTeamScheduleQuery>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<TeamScheduleResponse>.Success(response));

        var provided = await Create(kit).GetAsync(default);
        // The card's first request sends no date.
        var endpoint = await new PlannerController(kit.Mediator.Object).TeamDay(null, default);

        WidgetProviderTestKit.AssertSameBody(endpoint, provided);
        kit.Mediator.Verify(m => m.Send(It.Is<GetTeamScheduleQuery>(q => q.Date == null), It.IsAny<CancellationToken>()), Times.Exactly(2));
    }
}
