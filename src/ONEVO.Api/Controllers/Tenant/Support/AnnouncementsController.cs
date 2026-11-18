using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using ONEVO.Application.Features.DevPlatform.Support.Announcements;
using ONEVO.Domain.Features.DevPlatform.Support.Entities;

namespace ONEVO.Api.Controllers.Tenant.Support;

/// <summary>Published Crownus announcements for the tenant app banner. Any signed-in tenant user can read them.</summary>
[ApiController]
[Route("api/v1/announcements")]
[Authorize(Policy = "TenantPolicy")]
public sealed class AnnouncementsController : ControllerBase
{
    private readonly IMediator _mediator;

    public AnnouncementsController(IMediator mediator) => _mediator = mediator;

    [HttpGet("active")]
    public async Task<IActionResult> Active(CancellationToken ct)
    {
        var result = await _mediator.Send(new GetActiveAnnouncementsQuery(PlatformAnnouncement.AudienceTenants), ct);
        return result.IsSuccess ? Ok(result.Value) : Problem(result.Error, statusCode: result.StatusCode ?? 400);
    }
}
