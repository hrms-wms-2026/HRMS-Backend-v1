using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using ONEVO.Api.Filters;
using ONEVO.Application.Features.DevPlatform.Billing.Commands.CreateInvoice;
using ONEVO.Application.Features.DevPlatform.Billing.Commands.MarkInvoicePaid;
using ONEVO.Application.Features.DevPlatform.Billing.Commands.ResendInvoiceEmail;
using ONEVO.Application.Features.DevPlatform.Billing.Commands.VoidInvoice;
using ONEVO.Application.Features.DevPlatform.Billing.DTOs.Requests;
using ONEVO.Application.Features.DevPlatform.Billing.Queries.GetInvoice;
using ONEVO.Application.Features.DevPlatform.Billing.Queries.ListInvoices;
using ONEVO.Application.Features.DevPlatform.Billing.Queries.ListTenantInvoices;
using ONEVO.Application.Features.DevPlatform.PlatformAccess.Helpers;

namespace ONEVO.Api.Controllers.Admin.DevPlatform.Billing;

[ApiController]
[Route("admin/v1/invoices")]
public sealed class InvoicesController : ControllerBase
{
    private readonly IMediator _mediator;

    public InvoicesController(IMediator mediator) => _mediator = mediator;

    [HttpGet]
    [Authorize(Policy = "AdminPolicy")]
    [RequirePlatformPermission(PlatformPermissionCatalog.SubscriptionsRead)]
    public async Task<IActionResult> List(
        [FromQuery(Name = "tenant_id")] Guid? tenantId,
        [FromQuery] string? status,
        [FromQuery] DateTimeOffset? from,
        [FromQuery] DateTimeOffset? to,
        [FromQuery] int page = 1,
        [FromQuery(Name = "page_size")] int pageSize = 25,
        [FromQuery] int? skip = null,
        [FromQuery] int? take = null,
        CancellationToken ct = default)
    {
        var result = await _mediator.Send(
            new ListInvoicesQuery(tenantId, status, from, to, page, pageSize, skip, take),
            ct);

        return result.IsSuccess
            ? Ok(result.Value)
            : Problem(result.Error, statusCode: result.StatusCode ?? 400);
    }

    [HttpGet("{id:guid}")]
    [Authorize(Policy = "AdminPolicy")]
    [RequirePlatformPermission(PlatformPermissionCatalog.SubscriptionsRead)]
    public async Task<IActionResult> GetById(Guid id, CancellationToken ct)
    {
        var result = await _mediator.Send(new GetInvoiceQuery(id), ct);
        return result.IsSuccess
            ? Ok(result.Value)
            : Problem(result.Error, statusCode: result.StatusCode ?? 400);
    }

    [HttpGet("~/admin/v1/tenants/{tenantId:guid}/invoices")]
    [Authorize(Policy = "AdminPolicy")]
    [RequirePlatformPermission(PlatformPermissionCatalog.SubscriptionsRead)]
    public async Task<IActionResult> ListByTenant(Guid tenantId, CancellationToken ct)
    {
        var result = await _mediator.Send(new ListTenantInvoicesQuery(tenantId), ct);
        return result.IsSuccess
            ? Ok(result.Value)
            : Problem(result.Error, statusCode: result.StatusCode ?? 400);
    }

    [HttpPost]
    [Authorize(Policy = "AdminPolicy")]
    [RequirePlatformPermission(PlatformPermissionCatalog.SubscriptionsManage)]
    public async Task<IActionResult> Create(
        [FromBody] CreateInvoiceRequest request,
        CancellationToken ct)
    {
        var result = await _mediator.Send(new CreateInvoiceCommand(
            request.TenantId,
            request.TenantSubscriptionId,
            request.Currency,
            request.SubtotalAmount,
            request.TaxAmount,
            request.DiscountAmount,
            request.PeriodStart,
            request.PeriodEnd,
            request.IssuedAt,
            request.DueAt,
            request.Status), ct);

        return result.IsSuccess
            ? Ok(result.Value)
            : Problem(result.Error, statusCode: result.StatusCode ?? 400);
    }

    [HttpPatch("{id:guid}/mark-paid")]
    [Authorize(Policy = "AdminPolicy")]
    [RequirePlatformPermission(PlatformPermissionCatalog.SubscriptionsManage)]
    public async Task<IActionResult> MarkPaid(Guid id, CancellationToken ct)
    {
        var result = await _mediator.Send(new MarkInvoicePaidCommand(id), ct);
        return result.IsSuccess
            ? Ok(result.Value)
            : Problem(result.Error, statusCode: result.StatusCode ?? 400);
    }

    [HttpPatch("{id:guid}/void")]
    [Authorize(Policy = "AdminPolicy")]
    [RequirePlatformPermission(PlatformPermissionCatalog.SubscriptionsManage)]
    public async Task<IActionResult> Void(Guid id, CancellationToken ct)
    {
        var result = await _mediator.Send(new VoidInvoiceCommand(id), ct);
        return result.IsSuccess
            ? Ok(result.Value)
            : Problem(result.Error, statusCode: result.StatusCode ?? 400);
    }

    [HttpPost("{id:guid}/resend-email")]
    [Authorize(Policy = "AdminPolicy")]
    [RequirePlatformPermission(PlatformPermissionCatalog.SubscriptionsManage)]
    public async Task<IActionResult> ResendEmail(Guid id, CancellationToken ct)
    {
        var result = await _mediator.Send(new ResendInvoiceEmailCommand(id), ct);
        return result.IsSuccess
            ? Ok(result.Value)
            : Problem(result.Error, statusCode: result.StatusCode ?? 400);
    }
}
