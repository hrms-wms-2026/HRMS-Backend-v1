using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using ONEVO.Application.Features.Monitoring.Exceptions.Commands.AcknowledgeException;
using ONEVO.Application.Features.Monitoring.Exceptions.Commands.EscalateException;
using ONEVO.Application.Features.Monitoring.Exceptions.Commands.ResolveException;
using ONEVO.Application.Features.Monitoring.Exceptions.Queries.GetExceptionEvidence;
using ONEVO.Application.Features.Monitoring.Exceptions.Queries.GetExceptions;
using ONEVO.Domain.Features.Monitoring.Exceptions.Entities;

namespace ONEVO.Api.Controllers.Tenant.Monitoring.Exceptions;

/// <summary>
/// Exception alerts for reporting managers and HR. No [RequirePermission] here: access is
/// attendance:approve (manager), employees:write (HR) or the exceptions:* codes, and which
/// employees a caller may see comes from IEmployeeAuthorityResolver - both decided in the
/// handlers through IExceptionScopeResolver.
/// </summary>
[ApiController]
[Route("api/v1/monitoring/exceptions")]
[Authorize(Policy = "TenantPolicy")]
public class MonitoringExceptionsController : ControllerBase
{
    private readonly IMediator _mediator;

    public MonitoringExceptionsController(IMediator mediator) => _mediator = mediator;

    [HttpGet]
    public async Task<IActionResult> GetExceptions(
        [FromQuery] ExceptionStatus? status, [FromQuery] ExceptionType? type,
        [FromQuery] int page = 1, [FromQuery] int pageSize = 20, [FromQuery] bool activeOnly = false,
        CancellationToken ct = default)
    {
        var result = await _mediator.Send(
            new GetExceptionsQuery { Status = status, Type = type, Page = page, PageSize = pageSize, ActiveOnly = activeOnly }, ct);
        return result.IsSuccess ? Ok(result.Value) : Problem(result.Error, statusCode: result.StatusCode ?? 400);
    }

    /// <summary>Clock-in/out, face checks, check-in scans and the triggering photo (short-lived
    /// signed URL) for one case. Same visibility as the list.</summary>
    [HttpGet("{exceptionId:guid}/evidence")]
    public async Task<IActionResult> GetEvidence(Guid exceptionId, CancellationToken ct)
    {
        var result = await _mediator.Send(new GetExceptionEvidenceQuery(exceptionId), ct);
        return result.IsSuccess ? Ok(result.Value) : Problem(result.Error, statusCode: result.StatusCode ?? 400);
    }

    [HttpPost("{exceptionId:guid}/acknowledge")]
    public async Task<IActionResult> Acknowledge(Guid exceptionId, CancellationToken ct)
    {
        var result = await _mediator.Send(new AcknowledgeExceptionCommand(exceptionId), ct);
        return result.IsSuccess ? NoContent() : Problem(result.Error, statusCode: result.StatusCode ?? 400);
    }

    [HttpPost("{exceptionId:guid}/resolve")]
    public async Task<IActionResult> Resolve(
        Guid exceptionId, [FromBody] ExceptionActionRequest? body, CancellationToken ct)
    {
        var result = await _mediator.Send(new ResolveExceptionCommand(exceptionId, body?.Note), ct);
        return result.IsSuccess ? NoContent() : Problem(result.Error, statusCode: result.StatusCode ?? 400);
    }

    [HttpPost("{exceptionId:guid}/escalate")]
    public async Task<IActionResult> Escalate(
        Guid exceptionId, [FromBody] ExceptionActionRequest? body, CancellationToken ct)
    {
        var result = await _mediator.Send(new EscalateExceptionCommand(exceptionId, body?.Note), ct);
        return result.IsSuccess ? NoContent() : Problem(result.Error, statusCode: result.StatusCode ?? 400);
    }

    public sealed record ExceptionActionRequest(string? Note);
}
