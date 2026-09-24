using System.Security.Claims;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using ONEVO.Application.Features.Monitoring.ClientLogs.Commands.RecordClientLog;
using ONEVO.Application.Features.Monitoring.ClientLogs.DTOs.Requests;

namespace ONEVO.Api.Controllers.Admin.DevPlatform.Monitoring;

/// <summary>
/// Receives client-side error/log reports from the platform-administration console so they
/// land in the backend's own Serilog stream instead of being lost in the browser console. Any
/// authenticated admin may report their own client errors — this is not a permission-gated
/// resource, so there is deliberately no [RequirePlatformPermission] here.
/// </summary>
[ApiController]
[Route("admin/v1/logs")]
public sealed class ClientLogsController : ControllerBase
{
    private readonly IMediator _mediator;

    public ClientLogsController(IMediator mediator) => _mediator = mediator;

    [HttpPost]
    [Authorize(Policy = "AdminPolicy")]
    public async Task<IActionResult> Create([FromBody] RecordClientLogRequest request, CancellationToken ct)
    {
        var adminUserId = User.FindFirstValue(ClaimTypes.NameIdentifier) ?? string.Empty;
        var adminEmail = User.FindFirstValue(ClaimTypes.Email) ?? string.Empty;

        var result = await _mediator.Send(
            new RecordClientLogCommand(
                adminUserId,
                adminEmail,
                request.Level,
                request.Message,
                request.Context,
                request.Timestamp),
            ct);

        return result.IsSuccess
            ? NoContent()
            : Problem(result.Error, statusCode: result.StatusCode ?? 400);
    }
}
