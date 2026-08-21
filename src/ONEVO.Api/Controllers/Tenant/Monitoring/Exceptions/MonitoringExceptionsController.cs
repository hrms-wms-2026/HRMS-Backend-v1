using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using ONEVO.Api.Filters;
using ONEVO.Application.Features.Monitoring.Exceptions.Commands.AcknowledgeException;
using ONEVO.Application.Features.Monitoring.Exceptions.Commands.ResolveException;
using ONEVO.Application.Features.Monitoring.Exceptions.Queries.GetExceptions;
using ONEVO.Domain.Features.Monitoring.Exceptions.Entities;

namespace ONEVO.Api.Controllers.Tenant.Monitoring.Exceptions;

[ApiController]
[Route("api/v1/monitoring/exceptions")]
[Authorize(Policy = "TenantPolicy")]
public class MonitoringExceptionsController : ControllerBase
{
    private readonly IMediator _mediator;

    public MonitoringExceptionsController(IMediator mediator) => _mediator = mediator;

    [HttpGet]
    [RequirePermission("exceptions:view")]
    public async Task<IActionResult> GetExceptions(
        [FromQuery] ExceptionStatus? status, [FromQuery] ExceptionType? type,
        [FromQuery] int page = 1, [FromQuery] int pageSize = 20, CancellationToken ct = default)
    {
        var result = await _mediator.Send(
            new GetExceptionsQuery { Status = status, Type = type, Page = page, PageSize = pageSize }, ct);
        return result.IsSuccess ? Ok(result.Value) : Problem(result.Error, statusCode: result.StatusCode ?? 400);
    }

    [HttpPost("{exceptionId:guid}/acknowledge")]
    [RequirePermission("exceptions:acknowledge")]
    public async Task<IActionResult> Acknowledge(Guid exceptionId, CancellationToken ct)
    {
        var result = await _mediator.Send(new AcknowledgeExceptionCommand(exceptionId), ct);
        return result.IsSuccess ? NoContent() : Problem(result.Error, statusCode: result.StatusCode ?? 400);
    }

    [HttpPost("{exceptionId:guid}/resolve")]
    [RequirePermission("exceptions:acknowledge")]
    public async Task<IActionResult> Resolve(Guid exceptionId, CancellationToken ct)
    {
        var result = await _mediator.Send(new ResolveExceptionCommand(exceptionId), ct);
        return result.IsSuccess ? NoContent() : Problem(result.Error, statusCode: result.StatusCode ?? 400);
    }
}
