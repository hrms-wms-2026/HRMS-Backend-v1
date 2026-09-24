using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using ONEVO.Api.Filters;
using ONEVO.Application.Features.DevPlatform.Billing.Queries.GetTenantSubscription;
using ONEVO.Application.Features.DevPlatform.PlatformAccess.Helpers;

namespace ONEVO.Api.Controllers.Admin.DevPlatform.Billing;

[ApiController]
[Route("admin/v1/tenants/{tenantId:guid}/subscription")]
public sealed class TenantSubscriptionsController : ControllerBase
{
    private readonly IMediator _mediator;

    public TenantSubscriptionsController(IMediator mediator) => _mediator = mediator;

    [HttpGet]
    [Authorize(Policy = "AdminPolicy")]
    [RequirePlatformPermission(PlatformPermissionCatalog.SubscriptionsRead)]
    public async Task<IActionResult> Get(Guid tenantId, CancellationToken ct)
    {
        var result = await _mediator.Send(new GetTenantSubscriptionQuery(tenantId), ct);
        return result.IsSuccess
            ? Ok(result.Value)
            : Problem(result.Error, statusCode: result.StatusCode ?? 400);
    }
}
