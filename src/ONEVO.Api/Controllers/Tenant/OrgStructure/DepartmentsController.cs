using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using ONEVO.Api.Contracts.OrgStructure.Departments;
using ONEVO.Api.Filters;
using ONEVO.Application.Features.OrgStructure.Commands.ArchiveDepartment;
using ONEVO.Application.Features.OrgStructure.Commands.CreateDepartment;
using ONEVO.Application.Features.OrgStructure.Commands.RestoreDepartment;
using ONEVO.Application.Features.OrgStructure.Commands.UpdateDepartment;
using ONEVO.Application.Features.OrgStructure.Queries.CheckDepartmentArchiveDependencies;
using ONEVO.Application.Features.OrgStructure.Queries.GetDepartment;
using ONEVO.Application.Features.OrgStructure.Queries.ListDepartments;

namespace ONEVO.Api.Controllers.Tenant.OrgStructure;

[ApiController]
[Route("api/v1/org/legal-entities/{legalEntityId:guid}/departments")]
[Authorize(Policy = "TenantPolicy")]
public class DepartmentsController : ControllerBase
{
    private readonly IMediator _mediator;

    public DepartmentsController(IMediator mediator)
    {
        _mediator = mediator;
    }

    /// <summary>List departments for a specific legal entity. Supports search, sort, pagination,
    /// and an optional hierarchy (tree) view. Active departments only unless includeInactive=true.
    /// view=tree returns the full legal-entity hierarchy with search/includeInactive applied;
    /// parentDepartmentId, page, and pageSize are ignored in tree mode - see
    /// DEPARTMENT_HARDENING_PART3_LIST_SEARCH_PAGINATION_REPORT.md for the documented decision.</summary>
    [HttpGet]
    [RequirePermission("org:read")]
    public async Task<IActionResult> List(
        Guid legalEntityId,
        [FromQuery] string? search = null,
        [FromQuery] bool includeInactive = false,
        [FromQuery] Guid? parentDepartmentId = null,
        [FromQuery] string view = "flat",
        [FromQuery] string sortBy = "name",
        [FromQuery] string sortDirection = "asc",
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 25,
        CancellationToken ct = default)
    {
        var result = await _mediator.Send(
            new ListDepartmentsQuery(
                legalEntityId, search, includeInactive, parentDepartmentId, view, sortBy, sortDirection, page, pageSize),
            ct);

        if (!result.IsSuccess)
            return Problem(result.Error, statusCode: result.StatusCode ?? 400);

        object payload = result.Value!.Tree is not null ? result.Value.Tree : result.Value.Flat!;
        return Ok(payload);
    }

    /// <summary>Get a single department by Legal Entity ID and Department ID.</summary>
    [HttpGet("{departmentId:guid}")]
    [RequirePermission("org:read")]
    public async Task<IActionResult> Get(
        Guid legalEntityId,
        Guid departmentId,
        CancellationToken ct = default)
    {
        var result = await _mediator.Send(new GetDepartmentQuery(legalEntityId, departmentId), ct);
        return result.IsSuccess
            ? Ok(result.Value)
            : Problem(result.Error, statusCode: result.StatusCode ?? 400);
    }

    /// <summary>Create a department under a specific legal entity.</summary>
    [HttpPost]
    [RequirePermission("org:manage")]
    public async Task<IActionResult> Create(
        Guid legalEntityId,
        [FromBody] CreateDepartmentRequest request,
        CancellationToken ct = default)
    {
        var result = await _mediator.Send(
            new CreateDepartmentCommand(
                legalEntityId,
                request.Name,
                request.Code,
                request.ParentDepartmentId,
                request.HeadPositionId),
            ct);

        return result.IsSuccess
            ? CreatedAtAction(nameof(Get), new { legalEntityId, departmentId = result.Value!.Id }, result.Value)
            : Problem(result.Error, statusCode: result.StatusCode ?? 400);
    }

    /// <summary>Update department details. Legal Entity ID and Department ID are sourced from the route.</summary>
    [HttpPut("{departmentId:guid}")]
    [RequirePermission("org:manage")]
    public async Task<IActionResult> Update(
        Guid legalEntityId,
        Guid departmentId,
        [FromBody] UpdateDepartmentRequest request,
        CancellationToken ct = default)
    {
        var result = await _mediator.Send(
            new UpdateDepartmentCommand(
                legalEntityId,
                departmentId,
                request.Name,
                request.Code,
                request.ParentDepartmentId,
                request.HeadPositionId),
            ct);

        return result.IsSuccess
            ? Ok(result.Value)
            : Problem(result.Error, statusCode: result.StatusCode ?? 400);
    }

    /// <summary>Archives a department by setting IsActive = false. This is a soft
    /// deactivation, not a physical delete - audit history and hierarchy references
    /// remain intact. Prefer this endpoint for new integrations.</summary>
    [HttpPost("{departmentId:guid}/archive")]
    [RequirePermission("org:manage")]
    public async Task<IActionResult> Archive(
        Guid legalEntityId,
        Guid departmentId,
        CancellationToken ct = default)
    {
        var result = await _mediator.Send(new ArchiveDepartmentCommand(legalEntityId, departmentId), ct);
        return result.IsSuccess
            ? NoContent()
            : Problem(result.Error, statusCode: result.StatusCode ?? 400);
    }

    /// <summary>Checks whether a department can be archived: active subdepartments and
    /// active employees are counted from the database; active positions cannot be counted
    /// yet (Position has no DepartmentId column) and are reported as unsupported. Read-only -
    /// does not mutate the department.</summary>
    [HttpPost("{departmentId:guid}/archive-check")]
    [RequirePermission("org:read")]
    public async Task<IActionResult> ArchiveCheck(
        Guid legalEntityId,
        Guid departmentId,
        CancellationToken ct = default)
    {
        var result = await _mediator.Send(
            new CheckDepartmentArchiveDependenciesQuery(legalEntityId, departmentId), ct);
        return result.IsSuccess
            ? Ok(result.Value)
            : Problem(result.Error, statusCode: result.StatusCode ?? 400);
    }

    /// <summary>Restores an archived department by setting IsActive = true. Returns 409 if
    /// the parent department is missing or inactive. Never touches children, HeadPositionId,
    /// code, or name.</summary>
    [HttpPost("{departmentId:guid}/restore")]
    [RequirePermission("org:manage")]
    public async Task<IActionResult> Restore(
        Guid legalEntityId,
        Guid departmentId,
        CancellationToken ct = default)
    {
        var result = await _mediator.Send(new RestoreDepartmentCommand(legalEntityId, departmentId), ct);
        return result.IsSuccess
            ? NoContent()
            : Problem(result.Error, statusCode: result.StatusCode ?? 400);
    }

    /// <summary>Deprecated compatibility alias for Archive (POST .../archive). Kept so
    /// existing DELETE-based integrations keep working; delegates to the same
    /// ArchiveDepartmentCommand and performs the same soft-deactivation
    /// (IsActive = false) - never a physical delete. Prefer POST .../archive for new
    /// integrations.</summary>
    [HttpDelete("{departmentId:guid}")]
    [RequirePermission("org:manage")]
    public async Task<IActionResult> Delete(
        Guid legalEntityId,
        Guid departmentId,
        CancellationToken ct = default)
    {
        var result = await _mediator.Send(new ArchiveDepartmentCommand(legalEntityId, departmentId), ct);
        return result.IsSuccess
            ? NoContent()
            : Problem(result.Error, statusCode: result.StatusCode ?? 400);
    }
}
