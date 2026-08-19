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
using ONEVO.Api.Contracts.OrgStructure.Positions;
using ONEVO.Application.Features.OrgStructure.Queries.GetActiveHolders;
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

    /// <summary>Current active PrimaryEmployment holders of a position — used to disambiguate
    /// reporting-manager selection when the target position has multiple occupants.</summary>
    [HttpGet("{positionId:guid}/active-holders")]
    [RequirePermission("org:read")]
    public async Task<IActionResult> GetActiveHolders(
        Guid legalEntityId,
        Guid positionId,
        CancellationToken ct = default)
    {
        var result = await _mediator.Send(new GetActiveHoldersQuery(legalEntityId, positionId), ct);
        return result.IsSuccess
            ? Ok(result.Value!.Select(h => new ActiveHolderViewModel(h.EmployeeId, h.FirstName, h.LastName, h.WorkEmail, h.AvatarFileId)).ToList())
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

    /// <summary>Gets the System Access Role template for a position.</summary>
    [HttpGet("{positionId:guid}/access")]
    [RequirePermission("org:read")]
    public async Task<IActionResult> GetAccess(
        Guid legalEntityId,
        Guid positionId,
        CancellationToken ct = default)
    {
        var result = await _mediator.Send(new ONEVO.Application.Features.OrgStructure.Queries.GetPositionAccess.GetPositionAccessQuery(legalEntityId, positionId), ct);
        return result.IsSuccess
            ? Ok(result.Value)
            : Problem(result.Error, statusCode: result.StatusCode ?? 400);
    }

    /// <summary>Sets or updates the System Access Role template for a position.</summary>
    [HttpPut("{positionId:guid}/access")]
    [RequirePermission("org:manage")]
    public async Task<IActionResult> SetAccess(
        Guid legalEntityId,
        Guid positionId,
        [FromBody] ONEVO.Api.Contracts.OrgStructure.Positions.SetPositionAccessRequest request,
        CancellationToken ct = default)
    {
        var result = await _mediator.Send(
            new ONEVO.Application.Features.OrgStructure.Commands.SetPositionAccess.SetPositionAccessCommand(
                legalEntityId,
                positionId,
                request.RoleId,
                request.RequiresApproval),
            ct);

        return result.IsSuccess
            ? Ok(result.Value)
            : Problem(result.Error, statusCode: result.StatusCode ?? 400);
    }

    /// <summary>Gets management coverage records where this position is the owner/manager.</summary>
    [HttpGet("{positionId:guid}/coverage")]
    [RequirePermission("org:read")]
    public async Task<IActionResult> GetCoverage(
        Guid legalEntityId,
        Guid positionId,
        CancellationToken ct = default)
    {
        var result = await _mediator.Send(
            new ONEVO.Application.Features.OrgStructure.Queries.GetPositionCoverage.GetPositionCoverageQuery(legalEntityId, positionId),
            ct);

        return result.IsSuccess
            ? Ok(result.Value)
            : Problem(result.Error, statusCode: result.StatusCode ?? 400);
    }

    /// <summary>Adds a manual management coverage rule for this position.</summary>
    [HttpPost("{positionId:guid}/coverage")]
    [RequirePermission("org:manage")]
    public async Task<IActionResult> AddCoverage(
        Guid legalEntityId,
        Guid positionId,
        [FromBody] AddCoverageRecordRequest request,
        CancellationToken ct = default)
    {
        var result = await _mediator.Send(
            new ONEVO.Application.Features.OrgStructure.Commands.AddManualCoverageRecord.AddManualCoverageRecordCommand(
                legalEntityId,
                positionId,
                request.CoveredTargetType,
                request.CoveredPositionId,
                request.CoveredDepartmentId,
                request.OwnerOrder),
            ct);

        return result.IsSuccess
            ? Ok(result.Value)
            : Problem(result.Error, statusCode: result.StatusCode ?? 400);
    }

    /// <summary>Updates the responsibility level (ownerOrder) of a manual management coverage rule.</summary>
    [HttpPut("{positionId:guid}/coverage/{coverageId:guid}")]
    [RequirePermission("org:manage")]
    public async Task<IActionResult> UpdateCoverage(
        Guid legalEntityId,
        Guid positionId,
        Guid coverageId,
        [FromBody] ONEVO.Api.Contracts.OrgStructure.Positions.UpdateCoverageRecordRequest request,
        CancellationToken ct = default)
    {
        var result = await _mediator.Send(
            new ONEVO.Application.Features.OrgStructure.Commands.UpdateManualCoverageRecord.UpdateManualCoverageRecordCommand(
                legalEntityId,
                positionId,
                coverageId,
                request.OwnerOrder),
            ct);

        return result.IsSuccess
            ? Ok(result.Value)
            : Problem(result.Error, statusCode: result.StatusCode ?? 400);
    }

    /// <summary>Removes a management coverage rule for this position.</summary>
    [HttpPost("{positionId:guid}/coverage/{coverageId:guid}/remove")]
    [RequirePermission("org:manage")]
    public async Task<IActionResult> RemoveCoverage(
        Guid legalEntityId,
        Guid positionId,
        Guid coverageId,
        CancellationToken ct = default)
    {
        var result = await _mediator.Send(
            new ONEVO.Application.Features.OrgStructure.Commands.RemoveManualCoverageRecord.RemoveManualCoverageRecordCommand(
                legalEntityId,
                positionId,
                coverageId),
            ct);

        return result.IsSuccess
            ? NoContent()
            : Problem(result.Error, statusCode: result.StatusCode ?? 400);
    }
}

