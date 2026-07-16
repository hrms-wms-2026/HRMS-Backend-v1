using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using ONEVO.Application.Features.InfrastructureModule.CountryDefaults.Queries.GetCountryDefaults;

namespace ONEVO.Api.Controllers.Admin.DevPlatform.SystemReference;

[ApiController]
[Route("admin/v1/reference")]
[Authorize(Policy = "AdminPolicy")]
public sealed class AdminReferenceController : ControllerBase
{
    private readonly IMediator _mediator;

    public AdminReferenceController(IMediator mediator) => _mediator = mediator;

    [HttpGet("countries/{countryCode}/defaults")]
    public async Task<IActionResult> GetCountryDefaults(string countryCode, CancellationToken ct)
    {
        var result = await _mediator.Send(new GetCountryDefaultsQuery(countryCode), ct);
        if (!result.IsSuccess)
            return Problem(result.Error, statusCode: result.StatusCode ?? 400);
        return Ok(result.Value);
    }
}
