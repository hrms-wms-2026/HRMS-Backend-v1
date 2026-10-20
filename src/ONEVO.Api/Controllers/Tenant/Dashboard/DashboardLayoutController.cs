using System.Text.RegularExpressions;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using ONEVO.Api.Contracts.Dashboard;
using ONEVO.Application.Features.Dashboard.Layout.Commands.AddDashboardWidget;
using ONEVO.Application.Features.Dashboard.Layout.Commands.RemoveDashboardWidget;
using ONEVO.Application.Features.Dashboard.Layout.Commands.ResetDashboard;
using ONEVO.Application.Features.Dashboard.Layout.Commands.SaveDashboardLayout;
using ONEVO.Application.Features.Dashboard.Layout.Queries.GetAvailableWidgets;
using ONEVO.Application.Features.Dashboard.Layout.Queries.GetDashboardBootstrap;
using ONEVO.Application.Features.Dashboard.Layout.Queries.GetMyDashboards;
using ONEVO.Application.Features.Dashboard.Layout.Registry;

namespace ONEVO.Api.Controllers.Tenant.Dashboard;

/// <summary>
/// Customizable dashboard layouts (My Work / My Team). Ownership and widget visibility are enforced
/// in the handlers; the data endpoint for widget instances is added separately.
/// </summary>
[ApiController]
[Route("api/v1/dashboards")]
[Authorize(Policy = "TenantPolicy")]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public sealed partial class DashboardLayoutController(IMediator mediator) : ControllerBase
{
    [HttpGet("me")]
    public async Task<IActionResult> Me(CancellationToken ct = default)
    {
        var result = await mediator.Send(new GetMyDashboardsQuery(), ct);
        return result.IsSuccess ? Ok(result.Value) : Error(result.Error, result.StatusCode);
    }

    [HttpGet("{id:guid}/bootstrap")]
    public async Task<IActionResult> Bootstrap(
        Guid id, [FromQuery] string breakpoint = Breakpoints.Desktop, CancellationToken ct = default)
    {
        var result = await mediator.Send(new GetDashboardBootstrapQuery(id, breakpoint), ct);
        return result.IsSuccess ? Ok(result.Value) : Error(result.Error, result.StatusCode);
    }

    [HttpGet("{id:guid}/widgets/available")]
    public async Task<IActionResult> AvailableWidgets(
        Guid id, [FromQuery] string breakpoint = Breakpoints.Desktop, CancellationToken ct = default)
    {
        var result = await mediator.Send(new GetAvailableWidgetsQuery(id, breakpoint), ct);
        return result.IsSuccess ? Ok(result.Value) : Error(result.Error, result.StatusCode);
    }

    [HttpPost("{id:guid}/widgets")]
    public async Task<IActionResult> AddWidget(
        Guid id, [FromBody] AddDashboardWidgetRequest request, CancellationToken ct = default)
    {
        var result = await mediator.Send(
            new AddDashboardWidgetCommand(id, request.WidgetKey, request.ExpectedVersion, request.DesktopRect), ct);
        return result.IsSuccess
            ? StatusCode(StatusCodes.Status201Created, result.Value)
            : Error(result.Error, result.StatusCode);
    }

    [HttpDelete("{id:guid}/widgets/{instanceId:guid}")]
    public async Task<IActionResult> RemoveWidget(
        Guid id, Guid instanceId, [FromQuery] long expectedVersion, CancellationToken ct = default)
    {
        var result = await mediator.Send(new RemoveDashboardWidgetCommand(id, instanceId, expectedVersion), ct);
        return result.IsSuccess ? Ok(new { layoutVersion = result.Value }) : Error(result.Error, result.StatusCode);
    }

    [HttpPut("{id:guid}/layouts/{breakpoint}")]
    public async Task<IActionResult> SaveLayout(
        Guid id, string breakpoint, [FromBody] SaveDashboardLayoutRequest request, CancellationToken ct = default)
    {
        var result = await mediator.Send(
            new SaveDashboardLayoutCommand(id, breakpoint, request.ExpectedVersion, request.Widgets), ct);
        return result.IsSuccess ? Ok(new { layoutVersion = result.Value }) : Error(result.Error, result.StatusCode);
    }

    [HttpPost("{id:guid}/reset")]
    public async Task<IActionResult> Reset(
        Guid id, [FromBody] ResetDashboardRequest request, CancellationToken ct = default)
    {
        var result = await mediator.Send(new ResetDashboardCommand(id, request.ExpectedVersion), ct);
        return result.IsSuccess ? Ok(new { layoutVersion = result.Value }) : Error(result.Error, result.StatusCode);
    }

    /// <summary>
    /// Standard problem+json (title/status/detail) plus machine-readable extensions the client reads:
    /// code (first error code), codes (all codes) and currentVersion on version conflicts.
    /// </summary>
    private ObjectResult Error(string? message, int? statusCode)
    {
        var status = statusCode ?? StatusCodes.Status400BadRequest;
        var problem = new ProblemDetails { Status = status, Title = ReasonFor(status), Detail = message };

        if (message is not null)
        {
            var match = ErrorMessage().Match(message);
            if (match.Success)
            {
                var codes = match.Groups["codes"].Value.Split(',', StringSplitOptions.RemoveEmptyEntries);
                problem.Extensions["code"] = codes[0];
                problem.Extensions["codes"] = codes;
                if (match.Groups["version"].Success && long.TryParse(match.Groups["version"].Value, out var version))
                    problem.Extensions["currentVersion"] = version;
            }
        }

        return new ObjectResult(problem) { StatusCode = status };
    }

    private static string ReasonFor(int status) => status switch
    {
        StatusCodes.Status403Forbidden => "Forbidden",
        StatusCodes.Status404NotFound => "Not Found",
        StatusCodes.Status409Conflict => "Conflict",
        StatusCodes.Status422UnprocessableEntity => "Unprocessable Entity",
        _ => "Dashboard request failed.",
    };

    [GeneratedRegex(@"^(?<codes>[A-Z][A-Z_]+(?:,[A-Z][A-Z_]+)*)(?: currentVersion=(?<version>\d+))?$")]
    private static partial Regex ErrorMessage();
}
