using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using ONEVO.Api.Contracts.CoreHr.Onboarding;
using ONEVO.Api.Filters;
using ONEVO.Application.Features.CoreHr.Onboarding.Queries.CheckEmployeeNumberAvailability;
using ONEVO.Application.Features.CoreHr.Onboarding.Queries.GetEmployeeNumberSuggestion;

namespace ONEVO.Api.Controllers.Tenant.CoreHr;

/// <summary>Employee-number suggestion and live availability for Add Employee onboarding.
/// Tenant is always server-derived; suggestions are not reservations.</summary>
[ApiController]
[Route("api/v1/onboarding")]
[Authorize(Policy = "TenantPolicy")]
public sealed class OnboardingEmployeeNumberController(IMediator mediator) : ControllerBase
{
    /// <summary>Suggests the next <c>{COMPANY_CODE}-{NNNN}</c> employee number for the given
    /// legal entity. Does not reserve the value — save/finalize must re-check uniqueness.</summary>
    [HttpGet("employee-number-suggestion")]
    [RequirePermission("employees:write")]
    public async Task<IActionResult> Suggest(
        [FromQuery] Guid legalEntityId,
        CancellationToken ct = default)
    {
        var result = await mediator.Send(new GetEmployeeNumberSuggestionQuery(legalEntityId), ct);
        return result.IsSuccess
            ? Ok(new EmployeeNumberSuggestionViewModel(
                result.Value!.EmployeeNumber, result.Value.Prefix, result.Value.Sequence))
            : Problem(result.Error, statusCode: result.StatusCode ?? 400);
    }

    /// <summary>Checks whether an employee number is available in the current tenant.
    /// Hint only — final persistence still enforces the unique index.</summary>
    [HttpGet("employee-number-availability")]
    [RequirePermission("employees:write")]
    public async Task<IActionResult> Availability(
        [FromQuery] string? employeeNumber,
        CancellationToken ct = default)
    {
        var result = await mediator.Send(new CheckEmployeeNumberAvailabilityQuery(employeeNumber), ct);
        return result.IsSuccess
            ? Ok(new EmployeeNumberAvailabilityViewModel(
                result.Value!.EmployeeNumber, result.Value.Available))
            : Problem(result.Error, statusCode: result.StatusCode ?? 400);
    }
}
