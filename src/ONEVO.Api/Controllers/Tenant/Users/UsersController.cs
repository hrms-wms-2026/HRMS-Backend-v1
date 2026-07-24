using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using ONEVO.Application.Features.Users.Queries.GetMyProfile;

namespace ONEVO.Api.Controllers.Tenant.Users;

[ApiController]
[Route("api/v1/users")]
[Authorize(Policy = "TenantPolicy")]
public class UsersController : ControllerBase
{
    private readonly IMediator _mediator;

    public UsersController(IMediator mediator) => _mediator = mediator;

    /// <summary>
    /// Returns the authenticated user's full profile: employee details, enrolled devices,
    /// work location settings, and face scan (identity verification) status.
    /// </summary>
    [HttpGet("me")]
    public async Task<IActionResult> GetMe(CancellationToken ct)
    {
        var result = await _mediator.Send(new GetMyProfileQuery(), ct);
        return result.IsSuccess
            ? Ok(result.Value)
            : Problem(result.Error, statusCode: result.StatusCode ?? 400);
    }
}
