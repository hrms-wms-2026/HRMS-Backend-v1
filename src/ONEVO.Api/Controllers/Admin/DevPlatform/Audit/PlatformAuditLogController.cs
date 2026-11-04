using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using ONEVO.Api.Filters;
using ONEVO.Application.Features.DevPlatform.Audit.Queries.ListPlatformAuditLog;
using ONEVO.Application.Features.DevPlatform.PlatformAccess.Helpers;

namespace ONEVO.Api.Controllers.Admin.DevPlatform.Audit;

/// <summary>Read-only view of platform_audit_logs. There is deliberately no write endpoint.</summary>
[ApiController]
[Route("admin/v1/platform-audit-log")]
[Authorize(Policy = "AdminPolicy")]
public sealed class PlatformAuditLogController : ControllerBase
{
    private readonly IMediator _mediator;

    public PlatformAuditLogController(IMediator mediator) => _mediator = mediator;

    [HttpGet]
    [RequirePlatformPermission(PlatformPermissionCatalog.AuditRead)]
    public async Task<IActionResult> List(
        [FromQuery] int page = 1,
        [FromQuery(Name = "page_size")] int pageSize = 25,
        [FromQuery(Name = "actor_platform_user_id")] Guid? actorPlatformUserId = null,
        [FromQuery(Name = "action")] string? auditAction = null,
        [FromQuery(Name = "target_type")] string? targetType = null,
        [FromQuery(Name = "target_id")] string? targetId = null,
        [FromQuery(Name = "target_tenant_id")] Guid? targetTenantId = null,
        [FromQuery] DateTimeOffset? from = null,
        [FromQuery] DateTimeOffset? to = null,
        CancellationToken ct = default)
    {
        var result = await _mediator.Send(new ListPlatformAuditLogQuery(
            page, pageSize, actorPlatformUserId, auditAction, targetType, targetId, targetTenantId, from, to), ct);

        return result.IsSuccess
            ? Ok(result.Value)
            : Problem(result.Error, statusCode: result.StatusCode ?? 400);
    }
}
