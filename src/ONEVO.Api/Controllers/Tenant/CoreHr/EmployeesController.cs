using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using ONEVO.Api.Filters;
using ONEVO.Application.Features.CoreHr.Employee.Queries.GetEmployee;
using ONEVO.Application.Features.CoreHr.Employee.Queries.GetMyProfile;
using ONEVO.Application.Features.CoreHr.Employee.Queries.ListEmployees;

namespace ONEVO.Api.Controllers.Tenant.CoreHr;

[ApiController]
[Route("api/v1/employees")]
[Authorize(Policy = "TenantPolicy")]
public class EmployeesController : ControllerBase
{
    private readonly IMediator _mediator;

    public EmployeesController(IMediator mediator)
    {
        _mediator = mediator;
    }

    /// <summary>List employees visible to the caller. Tenant-scoped, paginated, and filtered
    /// by the caller's management-coverage-derived visibility scope unless they hold
    /// org:manage.</summary>
    [HttpGet]
    [RequirePermission("employees:read")]
    public async Task<IActionResult> List(
        [FromQuery] string? search = null,
        [FromQuery] Guid? departmentId = null,
        [FromQuery] Guid? legalEntityId = null,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 25,
        CancellationToken ct = default)
    {
        var result = await _mediator.Send(
            new ListEmployeesQuery(search, departmentId, legalEntityId, page, pageSize), ct);

        return result.IsSuccess
            ? Ok(result.Value)
            : Problem(result.Error, statusCode: result.StatusCode ?? 400);
    }

    /// <summary>Get a single employee by ID. Returns 404 if the employee does not exist in the
    /// caller's tenant, 403 if it exists but is outside the caller's visibility scope.</summary>
    [HttpGet("{id:guid}")]
    [RequirePermission("employees:read")]
    public async Task<IActionResult> GetById(Guid id, CancellationToken ct = default)
    {
        var result = await _mediator.Send(new GetEmployeeQuery(id), ct);

        return result.IsSuccess
            ? Ok(result.Value)
            : Problem(result.Error, statusCode: result.StatusCode ?? 400);
    }

    /// <summary>Composite read of the caller's own profile: personal info, job info (read-only),
    /// emergency contacts, dependents, masked payroll, and security status. Self-service only -
    /// no permission code required, matches profile-management.md's "authenticated self-service".</summary>
    [HttpGet("me")]
    public async Task<IActionResult> GetMyProfile(CancellationToken ct = default)
    {
        var result = await _mediator.Send(new GetMyProfileQuery(), ct);
        return result.IsSuccess
            ? Ok(result.Value)
            : Problem(result.Error, statusCode: result.StatusCode ?? 400);
    }
}
