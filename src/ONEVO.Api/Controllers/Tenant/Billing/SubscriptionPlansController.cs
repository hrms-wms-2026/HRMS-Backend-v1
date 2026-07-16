using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using ONEVO.Api.Filters;
using ONEVO.Application.Features.DevPlatform.Subscription.Queries.GetSubscriptionPlan;
using ONEVO.Application.Features.DevPlatform.Subscription.Queries.ListSubscriptionPlans;

namespace ONEVO.Api.Controllers.Tenant.Billing;

[ApiController]
[Route("api/v1/subscription-plans")]
[Authorize(Policy = "TenantPolicy")]
public sealed class SubscriptionPlansController : ControllerBase
{
    private readonly IMediator _mediator;

    public SubscriptionPlansController(IMediator mediator) => _mediator = mediator;

    /// <summary>List all active subscription plans.</summary>
    [HttpGet]
    [RequirePermission("billing:read")]
    public async Task<IActionResult> List(CancellationToken ct)
    {
        var result = await _mediator.Send(new ListSubscriptionPlansQuery(), ct);
        return result.IsSuccess
            ? Ok(result.Value)
            : Problem(result.Error, statusCode: result.StatusCode ?? 400);
    }

    /// <summary>Get a single subscription plan by id.</summary>
    [HttpGet("{id:guid}")]
    [RequirePermission("billing:read")]
    public async Task<IActionResult> Get(Guid id, CancellationToken ct)
    {
        var result = await _mediator.Send(new GetSubscriptionPlanQuery(id), ct);
        return result.IsSuccess
            ? Ok(result.Value)
            : Problem(result.Error, statusCode: result.StatusCode ?? 400);
    }
}
