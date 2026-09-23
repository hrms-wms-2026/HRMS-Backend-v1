using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using ONEVO.Application.Features.Monitoring.TrayActivation.Commands.ExchangeActivationCode;
using ONEVO.Application.Features.Monitoring.TrayActivation.Commands.GenerateActivationCode;
using ONEVO.Application.Features.Monitoring.TrayActivation.Commands.RecordTrayHeartbeat;
using ONEVO.Application.Features.Monitoring.TrayActivation.Commands.RefreshTrayToken;
using ONEVO.Application.Features.Monitoring.TrayActivation.Commands.RevokeDevice;
using ONEVO.Application.Features.Monitoring.TrayActivation.DTOs.Responses;
using ONEVO.Application.Features.Monitoring.TrayActivation.Queries.GetTrayPresence;

namespace ONEVO.Api.Controllers.Tenant.Monitoring.TrayActivation;

[ApiController]
[Route("api/v1/monitoring/activation")]
public class MonitoringActivationController : ControllerBase
{
    private readonly IMediator _mediator;
    public MonitoringActivationController(IMediator mediator)
    {
        _mediator = mediator;
    }

    [HttpPost("generate")]
    [Authorize(Policy = "TenantPolicy")]
    [ONEVO.Api.Middleware.AllowWithoutActiveTray]
    public async Task<IActionResult> Generate(CancellationToken ct)
    {
        var result = await _mediator.Send(new GenerateActivationCodeCommand(), ct);
        return result.IsSuccess ? Ok(result.Value) : Error(result);
    }

    [HttpPost("exchange")]
    [AllowAnonymous]
    public async Task<IActionResult> Exchange([FromBody] ExchangeRequest request, CancellationToken ct)
    {
        var result = await _mediator.Send(new ExchangeActivationCodeCommand(
            request.Code?.Trim().ToUpperInvariant() ?? string.Empty,
            request.DeviceName,
            request.DeviceOs,
            request.DeviceFingerprint), ct);
        return result.IsSuccess ? Ok(result.Value) : Error(result);
    }

    [HttpPost("refresh")]
    [AllowAnonymous]
    public async Task<IActionResult> Refresh([FromBody] RefreshRequest request, CancellationToken ct)
    {
        var result = await _mediator.Send(new RefreshTrayTokenCommand(
            request.RefreshToken, request.DeviceFingerprint), ct);
        return result.IsSuccess ? Ok(result.Value) : Error(result);
    }

    [HttpPost("heartbeat")]
    [Authorize(Policy = "TrayDevicePolicy")]
    public async Task<IActionResult> Heartbeat(CancellationToken ct)
    {
        var result = await _mediator.Send(new RecordTrayHeartbeatCommand(), ct);
        return result.IsSuccess ? NoContent() : Error(result);
    }

    [HttpGet("status")]
    [Authorize(Policy = "TenantPolicy")]
    [ONEVO.Api.Middleware.AllowWithoutActiveTray]
    public async Task<IActionResult> Status(CancellationToken ct)
    {
        var result = await _mediator.Send(new GetTrayPresenceQuery(), ct);
        return result.IsSuccess ? Ok(result.Value) : Error(result);
    }

    [HttpPost("revoke")]
    [Authorize(Policy = "TrayDevicePolicy")]
    public async Task<IActionResult> Revoke(CancellationToken ct)
    {
        var result = await _mediator.Send(new RevokeDeviceCommand(), ct);
        return result.IsSuccess ? NoContent() : Error(result);
    }

    private ObjectResult Error<T>(ONEVO.Application.Common.Models.Result<T> result)
    {
        var problem = new ProblemDetails
        {
            Status = result.StatusCode ?? StatusCodes.Status400BadRequest,
            Title = "Tray activation request failed.",
            Detail = result.Error,
        };
        if (!string.IsNullOrWhiteSpace(result.ErrorCode))
            problem.Extensions["code"] = result.ErrorCode;
        return new ObjectResult(problem) { StatusCode = problem.Status };
    }

    private ObjectResult Error(ONEVO.Application.Common.Models.Result result)
    {
        var problem = new ProblemDetails
        {
            Status = result.StatusCode ?? StatusCodes.Status400BadRequest,
            Title = "Tray activation request failed.",
            Detail = result.Error,
        };
        if (!string.IsNullOrWhiteSpace(result.ErrorCode))
            problem.Extensions["code"] = result.ErrorCode;
        return new ObjectResult(problem) { StatusCode = problem.Status };
    }

    public record ExchangeRequest(string Code, string DeviceName, string DeviceOs, string DeviceFingerprint);
    public record RefreshRequest(string RefreshToken, string DeviceFingerprint);
}
