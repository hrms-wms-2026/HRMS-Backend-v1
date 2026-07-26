using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using ONEVO.Api.Filters;
using ONEVO.Application.Features.DevPlatform.PlatformAccess.Helpers;
using ONEVO.Application.Features.DevPlatform.SystemConfig.PlatformProviders.Queries.ListPlatformProviders;

namespace ONEVO.Api.Controllers.Admin.DevPlatform.SystemConfig;

[ApiController]
[Authorize(Policy = "AdminPolicy")]
public sealed class PlatformProvidersController : ControllerBase
{
    private readonly IMediator _mediator;

    public PlatformProvidersController(IMediator mediator)
    {
        _mediator = mediator;
    }

    [HttpGet("admin/v1/system-config/providers")]
    [RequirePlatformPermission(PlatformPermissionCatalog.SystemConfigRead)]
    public async Task<IActionResult> ListProviders(CancellationToken cancellationToken)
    {
        var result = await _mediator.Send(
            new ListPlatformProvidersQuery(),
            cancellationToken);

        return result.IsSuccess
            ? Ok(result.Value)
            : Problem(result.Error, statusCode: result.StatusCode ?? 400);
    }
}
