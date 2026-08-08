using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using ONEVO.Api.Filters;
using ONEVO.Application.Features.DevPlatform.PlatformAccess.Helpers;
using ONEVO.Application.Features.DevPlatform.Subscription.Commands.ArchiveSubscriptionPlan;
using ONEVO.Application.Features.DevPlatform.Subscription.Commands.CreateSubscriptionPlan;
using ONEVO.Application.Features.DevPlatform.Subscription.Commands.UpdateSubscriptionPlan;
using ONEVO.Application.Features.DevPlatform.Subscription.DTOs.Requests;
using ONEVO.Application.Features.DevPlatform.Subscription.Queries.ListSubscriptionPlans;

namespace ONEVO.Api.Controllers.Admin.DevPlatform.Subscriptions;

[ApiController]
[Route("admin/v1/subscription-plans")]
public sealed class SubscriptionPlansController : ControllerBase
{
    private readonly IMediator _mediator;

    public SubscriptionPlansController(IMediator mediator) => _mediator = mediator;

    [HttpGet]
    [Authorize(Policy = "AdminPolicy")]
    [RequirePlatformPermission(PlatformPermissionCatalog.SubscriptionsRead)]
    public async Task<IActionResult> List(CancellationToken ct)
    {
        var result = await _mediator.Send(new ListSubscriptionPlansQuery(), ct);
        return result.IsSuccess
            ? Ok(result.Value)
            : Problem(result.Error, statusCode: result.StatusCode ?? 400);
    }

    [HttpPost]
    [Authorize(Policy = "AdminPolicy")]
    [RequirePlatformPermission(PlatformPermissionCatalog.SubscriptionsManage)]
    public async Task<IActionResult> Create(
        [FromBody] CreateSubscriptionPlanRequest request,
        CancellationToken ct)
    {
        var result = await _mediator.Send(new CreateSubscriptionPlanCommand(
            request.Name,
            request.Code,
            request.Tier,
            request.CompanySizeRange,
            request.ModuleKeys,
            request.Currency,
            request.OverrideMonthlyPrice,
            request.OverrideAnnualPrice,
            request.AiTokenLimitPerMonth,
            request.TrialPeriodDays,
            request.UnpaidGracePeriodDays), ct);

        return result.IsSuccess
            ? Ok(result.Value)
            : Problem(result.Error, statusCode: result.StatusCode ?? 400);
    }

    [HttpPatch("{id:guid}")]
    [Authorize(Policy = "AdminPolicy")]
    [RequirePlatformPermission(PlatformPermissionCatalog.SubscriptionsManage)]
    public async Task<IActionResult> Update(
        Guid id,
        [FromBody] UpdateSubscriptionPlanRequest request,
        CancellationToken ct)
    {
        var result = await _mediator.Send(new UpdateSubscriptionPlanCommand(
            id,
            request.Name,
            request.Tier,
            request.CompanySizeRange,
            request.ModuleKeys,
            request.Currency,
            request.OverrideMonthlyPrice,
            request.OverrideAnnualPrice,
            request.AiTokenLimitPerMonth,
            request.TrialPeriodDays,
            request.UnpaidGracePeriodDays), ct);

        return result.IsSuccess
            ? Ok(result.Value)
            : Problem(result.Error, statusCode: result.StatusCode ?? 400);
    }

    [HttpDelete("{id:guid}")]
    [Authorize(Policy = "AdminPolicy")]
    [RequirePlatformPermission(PlatformPermissionCatalog.SubscriptionsManage)]
    public async Task<IActionResult> Archive(Guid id, CancellationToken ct)
    {
        var result = await _mediator.Send(new ArchiveSubscriptionPlanCommand(id), ct);
        return result.IsSuccess
            ? NoContent()
            : Problem(result.Error, statusCode: result.StatusCode ?? 400);
    }
}
