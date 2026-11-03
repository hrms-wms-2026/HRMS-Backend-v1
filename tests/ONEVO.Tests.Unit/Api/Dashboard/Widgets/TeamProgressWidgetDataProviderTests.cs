using Moq;
using ONEVO.Api.Controllers.Tenant.WorkManagement;
using ONEVO.Api.Dashboard.Widgets;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Features.WorkManagement.Leadership.DTOs;
using ONEVO.Application.Features.WorkManagement.Leadership.Queries.GetLedWorkProgress;
using Xunit;

namespace ONEVO.Tests.Unit.Api.Dashboard.Widgets;

public sealed class TeamProgressWidgetDataProviderTests
{
    private static TeamProgressWidgetDataProvider Create(WidgetProviderTestKit kit) =>
        new(kit.Mediator.Object, kit.User.Object, kit.Entitlements.Object);

    [Fact]
    public async Task Missing_permission_is_forbidden()
    {
        // WorkLeadershipController is gated by [RequireAnyModule(<Work Management modules>)].
        var kit = new WidgetProviderTestKit(activeModules: []);

        var result = await Create(kit).GetAsync(default);

        WidgetProviderTestKit.AssertForbidden(result);
        kit.Mediator.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task Returns_same_view_model_as_endpoint()
    {
        var kit = new WidgetProviderTestKit(activeModules: ["projects"]);
        var hours = new LedHourTotals(10, 4, 3, 2, 1, 0, 5, 1);
        var projectId = Guid.NewGuid();
        var objectiveId = Guid.NewGuid();
        var response = new LedWorkProgressResponse(
            hours,
            [new LedProjectProgress(projectId, "Website", "WEB", hours,
                [new LedModuleProgress(objectiveId, "Launch", true, new DateOnly(2026, 11, 1), null, 0, true, hours)])],
            [new LedOverdueTask(Guid.NewGuid(), "WEB-3", "Fix", projectId, objectiveId, new DateOnly(2026, 10, 18), 2,
                [new LedTaskAssignee(Guid.NewGuid(), "Ada Lovelace")])],
            1, 1);
        kit.Mediator.Setup(m => m.Send(It.IsAny<GetLedWorkProgressQuery>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<LedWorkProgressResponse>.Success(response));

        var provided = await Create(kit).GetAsync(default);
        // MyTeamLedProgressApiService.getLedProgress() sends overdueLimit=10 (store passes nothing).
        var endpoint = await new WorkLeadershipController(kit.Mediator.Object).LedProgress(10, default);

        WidgetProviderTestKit.AssertSameBody(endpoint, provided);
        kit.Mediator.Verify(m => m.Send(It.Is<GetLedWorkProgressQuery>(q => q.OverdueLimit == 10), It.IsAny<CancellationToken>()),
            Times.Exactly(2));
    }
}
