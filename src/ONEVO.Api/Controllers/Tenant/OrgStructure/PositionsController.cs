using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using ONEVO.Api.Contracts.OrgStructure.Positions;
using ONEVO.Api.Filters;
using ONEVO.Application.Features.OrgStructure.Commands.ArchivePosition;
using ONEVO.Application.Features.OrgStructure.Commands.CheckPositionArchive;
using ONEVO.Application.Features.OrgStructure.Commands.CreatePosition;
using ONEVO.Application.Features.OrgStructure.Commands.RestorePosition;
using ONEVO.Application.Features.OrgStructure.Commands.UpdatePosition;
using ONEVO.Application.Features.OrgStructure.Queries.GetPositionById;
using ONEVO.Application.Features.OrgStructure.Queries.GetPositionTree;
using ONEVO.Application.Features.OrgStructure.Queries.ListPositions;

namespace ONEVO.Api.Controllers.Tenant.OrgStructure;

[ApiController]
[Route("api/v1/org/legal-entities/{legalEntityId:guid}/positions")]
[Authorize(Policy = "TenantPolicy")]
public class PositionsController : ControllerBase
{
    private readonly IMediator _mediator;

    public PositionsController(IMediator mediator)
    {
        _mediator = mediator;
    }

    /// <summary>List positions for a specific legal entity. Supports filtering by department,
    /// search, sort, and pagination. Active positions only unless includeInactive=true.</summary>
    [HttpGet]
    [RequirePermission("org:read")]
    public async Task<IActionResult> List(
        Guid legalEntityId,
        [FromQuery] Guid? departmentId = null,
        [FromQuery] string? search = null,
        [FromQuery] bool includeInactive = false,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 25,
        [FromQuery] string sortBy = "name",
        [FromQuery] string sortDirection = "asc",
        CancellationToken ct = default)
    {
        var result = await _mediator.Send(
            new ListPositionsQuery(
                legalEntityId, departmentId, search, includeInactive, page, pageSize, sortBy, sortDirection),
            ct);

        return result.IsSuccess
            ? Ok(result.Value)
            : Problem(result.Error, statusCode: result.StatusCode ?? 400);
    }

    /// <summary>Get the full position hierarchy for a legal entity.</summary>
    [HttpGet("tree")]
    [RequirePermission("org:read")]
    public async Task<IActionResult> Tree(
        Guid legalEntityId,
        [FromQuery] bool includeInactive = false,
        CancellationToken ct = default)
    {
        var result = await _mediator.Send(new GetPositionTreeQuery(legalEntityId, includeInactive), ct);
        return result.IsSuccess
            ? Ok(result.Value)
            : Problem(result.Error, statusCode: result.StatusCode ?? 400);
    }

    /// <summary>Get a single position by Legal Entity ID and Position ID.</summary>
    [HttpGet("{positionId:guid}")]
    [RequirePermission("org:read")]
    public async Task<IActionResult> Get(
        Guid legalEntityId,
        Guid positionId,
        CancellationToken ct = default)
    {
        var result = await _mediator.Send(new GetPositionByIdQuery(legalEntityId, positionId), ct);
        return result.IsSuccess
            ? Ok(result.Value)
            : Problem(result.Error, statusCode: result.StatusCode ?? 400);
    }

    /// <summary>Create a position under a specific legal entity.</summary>
    [HttpPost]
    [RequirePermission("org:manage")]
    public async Task<IActionResult> Create(
        Guid legalEntityId,
        [FromBody] CreatePositionRequest request,
        CancellationToken ct = default)
    {
        var result = await _mediator.Send(
            new CreatePositionCommand(
                legalEntityId,
                request.DepartmentId,
                request.Name,
                request.Code,
                request.PositionType,
                request.MaxOccupancy,
                request.ReportsToPositionId),
            ct);

        return result.IsSuccess
            ? CreatedAtAction(nameof(Get), new { legalEntityId, positionId = result.Value!.Id }, result.Value)
            : Problem(result.Error, statusCode: result.StatusCode ?? 400);
    }

    /// <summary>Update position details. Legal Entity ID and Position ID are sourced from the route.</summary>
    [HttpPut("{positionId:guid}")]
    [RequirePermission("org:manage")]
    public async Task<IActionResult> Update(
        Guid legalEntityId,
        Guid positionId,
        [FromBody] UpdatePositionRequest request,
        CancellationToken ct = default)
    {
        var result = await _mediator.Send(
            new UpdatePositionCommand(
                legalEntityId,
                positionId,
                request.DepartmentId,
                request.Name,
                request.Code,
                request.PositionType,
                request.MaxOccupancy,
                request.ReportsToPositionId),
            ct);

        return result.IsSuccess
            ? Ok(result.Value)
            : Problem(result.Error, statusCode: result.StatusCode ?? 400);
    }

    /// <summary>Checks whether a position can be archived. Read-only - does not mutate the position.</summary>
    [HttpPost("{positionId:guid}/archive-check")]
    [RequirePermission("org:read")]
    public async Task<IActionResult> ArchiveCheck(
        Guid legalEntityId,
        Guid positionId,
        CancellationToken ct = default)
    {
        var result = await _mediator.Send(new CheckPositionArchiveCommand(legalEntityId, positionId), ct);
        return result.IsSuccess
            ? Ok(result.Value)
            : Problem(result.Error, statusCode: result.StatusCode ?? 400);
    }

    /// <summary>Archives a position by setting IsActive = false. Soft deactivation only, not a physical delete.</summary>
    [HttpPost("{positionId:guid}/archive")]
    [RequirePermission("org:manage")]
    public async Task<IActionResult> Archive(
        Guid legalEntityId,
        Guid positionId,
        CancellationToken ct = default)
    {
        var result = await _mediator.Send(new ArchivePositionCommand(legalEntityId, positionId), ct);
        return result.IsSuccess
            ? NoContent()
            : Problem(result.Error, statusCode: result.StatusCode ?? 400);
    }

    /// <summary>Restores an archived position by setting IsActive = true.</summary>
    [HttpPost("{positionId:guid}/restore")]
    [RequirePermission("org:manage")]
    public async Task<IActionResult> Restore(
        Guid legalEntityId,
        Guid positionId,
        CancellationToken ct = default)
    {
        var result = await _mediator.Send(new RestorePositionCommand(legalEntityId, positionId), ct);
        return result.IsSuccess
            ? NoContent()
            : Problem(result.Error, statusCode: result.StatusCode ?? 400);
    }
}
