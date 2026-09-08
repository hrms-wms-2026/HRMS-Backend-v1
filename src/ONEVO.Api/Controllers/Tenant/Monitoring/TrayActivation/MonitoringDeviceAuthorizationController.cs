using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using ONEVO.Application.Features.Monitoring.TrayActivation.Commands.ApproveDeviceAuthorization;
using ONEVO.Application.Features.Monitoring.TrayActivation.Commands.PollDeviceAuthorization;
using ONEVO.Application.Features.Monitoring.TrayActivation.Commands.StartDeviceAuthorization;
using ONEVO.Application.Features.Monitoring.TrayActivation.DTOs.Requests;
using ONEVO.Application.Features.Monitoring.TrayActivation.Queries.GetDeviceAuthorizationPreview;

namespace ONEVO.Api.Controllers.Tenant.Monitoring.TrayActivation;

[ApiController]
[Route("api/v1/monitoring/device-authorization")]
public sealed class MonitoringDeviceAuthorizationController : ControllerBase
{
    private readonly IMediator _mediator;

    public MonitoringDeviceAuthorizationController(IMediator mediator)
    {
        _mediator = mediator;
    }

    [HttpPost("start")]
    [AllowAnonymous]
    [ONEVO.Api.Middleware.AllowWithoutActiveTray]
    public async Task<IActionResult> Start(
        [FromBody] StartDeviceAuthorizationRequest request,
        CancellationToken ct)
    {
        var result = await _mediator.Send(new StartDeviceAuthorizationCommand(
            request.DeviceName,
            request.DeviceOs,
            request.DeviceFingerprint,
            request.ClientVersion), ct);
        return result.IsSuccess ? Ok(result.Value) : Error(result);
    }

    [HttpGet("{requestId:guid}")]
    [Authorize(Policy = "TenantPolicy")]
    [ONEVO.Api.Middleware.AllowWithoutActiveTray]
    public async Task<IActionResult> Preview(
        Guid requestId,
        [FromQuery(Name = "user_code")] string userCode,
        CancellationToken ct)
    {
        var result = await _mediator.Send(
            new GetDeviceAuthorizationPreviewQuery(requestId, userCode), ct);
        return result.IsSuccess ? Ok(result.Value) : Error(result);
    }

    [HttpPost("approve")]
    [Authorize(Policy = "TenantPolicy")]
    [ONEVO.Api.Middleware.AllowWithoutActiveTray]
    public async Task<IActionResult> Approve(
        [FromBody] ApproveDeviceAuthorizationRequest request,
        CancellationToken ct)
    {
        var result = await _mediator.Send(new ApproveDeviceAuthorizationCommand(
            request.RequestId,
            request.UserCode), ct);
        return result.IsSuccess ? NoContent() : Error(result);
    }

    [HttpPost("token")]
    [AllowAnonymous]
    [ONEVO.Api.Middleware.AllowWithoutActiveTray]
    public async Task<IActionResult> Token(
        [FromBody] PollDeviceAuthorizationRequest request,
        CancellationToken ct)
    {
        var result = await _mediator.Send(new PollDeviceAuthorizationCommand(
            request.DeviceCode,
            request.DeviceFingerprint), ct);
        return result.IsSuccess ? Ok(result.Value) : Error(result);
    }

    private ObjectResult Error<T>(ONEVO.Application.Common.Models.Result<T> result)
    {
        var problem = new ProblemDetails
        {
            Status = result.StatusCode ?? StatusCodes.Status400BadRequest,
            Title = "Device authorization request failed.",
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
            Title = "Device authorization request failed.",
            Detail = result.Error,
        };
        if (!string.IsNullOrWhiteSpace(result.ErrorCode))
            problem.Extensions["code"] = result.ErrorCode;
        return new ObjectResult(problem) { StatusCode = problem.Status };
    }
}
