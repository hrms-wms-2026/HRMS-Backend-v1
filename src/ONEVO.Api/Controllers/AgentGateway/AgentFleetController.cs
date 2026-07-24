using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using ONEVO.Application.Features.AgentGateway.Queries.GetAgentHealthList;

namespace ONEVO.Api.Controllers.AgentGateway;

[ApiController]
[Route("api/v1/agents")]
[Authorize]
public class AgentFleetController : ControllerBase
{
    private readonly IMediator _mediator;
    public AgentFleetController(IMediator mediator) => _mediator = mediator;

    /// <summary>Fleet health list — all active agents for this tenant.</summary>
    [HttpGet]
    public async Task<IActionResult> GetFleet(CancellationToken ct)
    {
        var result = await _mediator.Send(new GetAgentHealthListQuery(), ct);
        if (!result.IsSuccess) return Problem(result.Error, statusCode: result.StatusCode ?? 400);
        return Ok(result.Value);
    }
}
