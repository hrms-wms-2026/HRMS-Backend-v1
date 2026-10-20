using MediatR;
using Microsoft.AspNetCore.Mvc;
using Moq;
using ONEVO.Api.Contracts.Dashboard;
using ONEVO.Api.Controllers.Tenant.Dashboard;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Features.Dashboard.Layout.Commands.AddDashboardWidget;
using ONEVO.Application.Features.Dashboard.Layout.Commands.RemoveDashboardWidget;
using ONEVO.Application.Features.Dashboard.Layout.Commands.SaveDashboardLayout;
using ONEVO.Application.Features.Dashboard.Layout.Services;
using Xunit;

namespace ONEVO.Tests.Unit.Features.Dashboard.Layout;

public sealed class DashboardLayoutControllerTests
{
    private static readonly Guid DashboardId = Guid.NewGuid();

    private static (DashboardLayoutController Controller, Mock<IMediator> Mediator) Create()
    {
        var mediator = new Mock<IMediator>();
        return (new DashboardLayoutController(mediator.Object), mediator);
    }

    private static ProblemDetails Problem(IActionResult result, int status)
    {
        var obj = Assert.IsType<ObjectResult>(result);
        Assert.Equal(status, obj.StatusCode);
        var problem = Assert.IsType<ProblemDetails>(obj.Value);
        Assert.Equal(status, problem.Status);
        return problem;
    }

    [Fact]
    public async Task Version_conflict_exposes_code_and_current_version()
    {
        var (controller, mediator) = Create();
        mediator.Setup(m => m.Send(It.IsAny<RemoveDashboardWidgetCommand>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(DashboardLayoutSupport.Conflict<long>(7));

        var problem = Problem(await controller.RemoveWidget(DashboardId, Guid.NewGuid(), 3), 409);

        Assert.Equal("LAYOUT_VERSION_CONFLICT currentVersion=7", problem.Detail);
        Assert.Equal("LAYOUT_VERSION_CONFLICT", problem.Extensions["code"]);
        Assert.Equal(7L, problem.Extensions["currentVersion"]);
    }

    [Fact]
    public async Task Layout_validation_failure_exposes_codes()
    {
        var (controller, mediator) = Create();
        mediator.Setup(m => m.Send(It.IsAny<SaveDashboardLayoutCommand>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<long>.UnprocessableEntity("OVERLAP,SIZE_NOT_ALLOWED"));

        var problem = Problem(
            await controller.SaveLayout(DashboardId, "desktop", new SaveDashboardLayoutRequest(1, []), default), 422);

        Assert.Equal("OVERLAP", problem.Extensions["code"]);
        Assert.Equal(new[] { "OVERLAP", "SIZE_NOT_ALLOWED" }, (string[])problem.Extensions["codes"]!);
        Assert.False(problem.Extensions.ContainsKey("currentVersion"));
    }

    [Fact]
    public async Task Free_text_error_has_no_code_extension()
    {
        var (controller, mediator) = Create();
        mediator.Setup(m => m.Send(It.IsAny<SaveDashboardLayoutCommand>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<long>.NotFound("Dashboard not found."));

        var problem = Problem(
            await controller.SaveLayout(DashboardId, "desktop", new SaveDashboardLayoutRequest(1, []), default), 404);

        Assert.Equal("Dashboard not found.", problem.Detail);
        Assert.False(problem.Extensions.ContainsKey("code"));
    }

    [Fact]
    public async Task Add_widget_returns_201_with_result()
    {
        var (controller, mediator) = Create();
        var dto = new AddWidgetResultDto(Guid.NewGuid(), 5);
        mediator.Setup(m => m.Send(
                It.Is<AddDashboardWidgetCommand>(c => c.DashboardId == DashboardId && c.WidgetKey == "my-tasks" && c.ExpectedVersion == 4),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<AddWidgetResultDto>.Success(dto));

        var result = await controller.AddWidget(DashboardId, new AddDashboardWidgetRequest("my-tasks", 4, null), default);

        var obj = Assert.IsType<ObjectResult>(result);
        Assert.Equal(201, obj.StatusCode);
        Assert.Same(dto, obj.Value);
    }
}
