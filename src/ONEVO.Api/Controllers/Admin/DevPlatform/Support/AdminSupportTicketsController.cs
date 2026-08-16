using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using ONEVO.Api.Filters;
using ONEVO.Application.Features.DevPlatform.PlatformAccess.Helpers;
using ONEVO.Application.Features.DevPlatform.PlatformAccess.ServiceInterfaces;
using ONEVO.Application.Features.DevPlatform.Support.Commands.AddSupportTicketComment;
using ONEVO.Application.Features.DevPlatform.Support.Commands.CreateSupportTicket;
using ONEVO.Application.Features.DevPlatform.Support.Commands.UpdateSupportTicketStatus;
using ONEVO.Application.Features.DevPlatform.Support.DTOs.Requests;
using ONEVO.Application.Features.DevPlatform.Support.Queries.GetSupportTicketDetail;
using ONEVO.Application.Features.DevPlatform.Support.Queries.ListSupportTickets;

namespace ONEVO.Api.Controllers.Admin.DevPlatform.Support;

[ApiController]
[Authorize(Policy = "AdminPolicy")]
public sealed class AdminSupportTicketsController : ControllerBase
{
    private readonly IMediator _mediator;
    private readonly ICurrentPlatformUserContext _currentUser;

    public AdminSupportTicketsController(IMediator mediator, ICurrentPlatformUserContext currentUser)
    {
        _mediator = mediator;
        _currentUser = currentUser;
    }

    [HttpGet("admin/v1/support/tickets")]
    [RequirePlatformPermission(PlatformPermissionCatalog.SupportRead)]
    public async Task<IActionResult> List(
        [FromQuery] string? status,
        [FromQuery] string? priority,
        [FromQuery(Name = "tenant_id")] Guid? tenantId,
        [FromQuery] int page = 1,
        [FromQuery(Name = "page_size")] int pageSize = 25,
        CancellationToken ct = default)
    {
        var result = await _mediator.Send(new ListSupportTicketsQuery(status, priority, tenantId, page, pageSize), ct);
        return result.IsSuccess
            ? Ok(result.Value)
            : Problem(result.Error, statusCode: result.StatusCode ?? 400);
    }

    [HttpGet("admin/v1/support/tickets/{ticketId:guid}")]
    [RequirePlatformPermission(PlatformPermissionCatalog.SupportRead)]
    public async Task<IActionResult> GetById(Guid ticketId, CancellationToken ct)
    {
        var result = await _mediator.Send(new GetSupportTicketDetailQuery(ticketId), ct);
        return result.IsSuccess
            ? Ok(result.Value)
            : Problem(result.Error, statusCode: result.StatusCode ?? 400);
    }

    [HttpPost("admin/v1/support/tickets")]
    [RequirePlatformPermission(PlatformPermissionCatalog.SupportManage)]
    public async Task<IActionResult> Create([FromBody] CreateSupportTicketRequest request, CancellationToken ct)
    {
        var result = await _mediator.Send(
            new CreateSupportTicketCommand(
                request.TenantId,
                request.Subject,
                request.Description,
                request.Priority,
                request.Category,
                _currentUser.UserId),
            ct);
        return result.IsSuccess
            ? Created($"admin/v1/support/tickets/{result.Value!.Id}", result.Value)
            : Problem(result.Error, statusCode: result.StatusCode ?? 400);
    }

    [HttpPatch("admin/v1/support/tickets/{ticketId:guid}/status")]
    [RequirePlatformPermission(PlatformPermissionCatalog.SupportManage)]
    public async Task<IActionResult> UpdateStatus(
        Guid ticketId,
        [FromBody] UpdateSupportTicketStatusRequest request,
        CancellationToken ct)
    {
        var result = await _mediator.Send(new UpdateSupportTicketStatusCommand(ticketId, request.Status), ct);
        return result.IsSuccess
            ? Ok(result.Value)
            : Problem(result.Error, statusCode: result.StatusCode ?? 400);
    }

    [HttpPost("admin/v1/support/tickets/{ticketId:guid}/comments")]
    [RequirePlatformPermission(PlatformPermissionCatalog.SupportManage)]
    public async Task<IActionResult> AddComment(
        Guid ticketId,
        [FromBody] AddSupportTicketCommentRequest request,
        CancellationToken ct)
    {
        var result = await _mediator.Send(
            new AddSupportTicketCommentCommand(ticketId, request.Body, request.IsInternal, _currentUser.UserId),
            ct);
        return result.IsSuccess
            ? Created($"admin/v1/support/tickets/{ticketId}", result.Value)
            : Problem(result.Error, statusCode: result.StatusCode ?? 400);
    }
}
