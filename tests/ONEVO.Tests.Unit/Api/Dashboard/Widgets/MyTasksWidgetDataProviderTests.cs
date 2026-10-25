using Moq;
using ONEVO.Api.Controllers.Tenant.WorkManagement;
using ONEVO.Api.Dashboard.Widgets;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Features.WorkManagement.Tasks.DTOs.Responses;
using ONEVO.Application.Features.WorkManagement.Tasks.Queries.GetMyActiveTasks;
using Xunit;

namespace ONEVO.Tests.Unit.Api.Dashboard.Widgets;

public sealed class MyTasksWidgetDataProviderTests
{
    private static MyTasksWidgetDataProvider Create(WidgetProviderTestKit kit) =>
        new(kit.Mediator.Object, kit.User.Object, kit.Entitlements.Object);

    [Fact]
    public async Task Missing_permission_is_forbidden()
    {
        // TasksController is gated by [RequireAnyModule(<Work Management modules>)].
        var kit = new WidgetProviderTestKit(activeModules: ["attendance"]);

        var result = await Create(kit).GetAsync(default);

        WidgetProviderTestKit.AssertForbidden(result);
        kit.Mediator.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task Returns_same_view_model_as_endpoint()
    {
        var kit = new WidgetProviderTestKit(activeModules: ["TASKS"]);
        var response = new MyTasksResponse([
            new MyTaskItem(Guid.NewGuid(), "WEB-12", "Ship it", new DateOnly(2026, 10, 19), true,
                Guid.NewGuid(), "Website", Guid.NewGuid(), "high"),
        ]);
        kit.Mediator.Setup(m => m.Send(It.IsAny<GetMyActiveTasksQuery>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<MyTasksResponse>.Success(response));

        var provided = await Create(kit).GetAsync(default);
        // MyTasksApiService.getMyTasks() sends upcomingDays=7 (its default; my-tasks-card passes nothing).
        var endpoint = await new TasksController(kit.Mediator.Object).MyTasks(7, default);

        WidgetProviderTestKit.AssertSameBody(endpoint, provided);
        kit.Mediator.Verify(m => m.Send(It.Is<GetMyActiveTasksQuery>(q => q.UpcomingDays == 7), It.IsAny<CancellationToken>()),
            Times.Exactly(2));
    }

    [Fact]
    public async Task Handler_failure_keeps_status()
    {
        var kit = new WidgetProviderTestKit(activeModules: ["tasks"]);
        kit.Mediator.Setup(m => m.Send(It.IsAny<GetMyActiveTasksQuery>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<MyTasksResponse>.Forbidden("No employee record for the current user."));

        var result = await Create(kit).GetAsync(default);

        Assert.False(result.IsSuccess);
        Assert.Equal(403, result.StatusCode);
        Assert.Equal("No employee record for the current user.", result.Error);
    }
}
