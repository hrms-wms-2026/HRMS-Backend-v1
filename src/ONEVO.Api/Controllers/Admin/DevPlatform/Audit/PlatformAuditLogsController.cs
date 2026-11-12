using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using ONEVO.Api.Filters;
using ONEVO.Application.Features.DevPlatform.Audit.Queries.ListPlatformAuditLogs;
using ONEVO.Application.Features.DevPlatform.PlatformAccess.Helpers;

namespace ONEVO.Api.Controllers.Admin.DevPlatform.Audit;

/// <summary>
/// Read-only view of the privileged-action audit trail (platform_audit_logs). There is no write,
/// update or delete endpoint: rows are written only by AuditingBehavior.
/// </summary>
[ApiController]
[Route("admin/v1/audit-logs")]
[Authorize(Policy = "AdminPolicy")]
[RequirePlatformPermission(PlatformPermissionCatalog.AuditRead)]
public sealed class PlatformAuditLogsController : ControllerBase
{
    private readonly IMediator _mediator;

    public PlatformAuditLogsController(IMediator mediator) => _mediator = mediator;

    [HttpGet]
    public async Task<IActionResult> List(
        [FromQuery] int page = 1,
        [FromQuery(Name = "page_size")] int pageSize = 25,
        [FromQuery(Name = "actor_id")] Guid? actorId = null,
        [FromQuery(Name = "action")] string? auditAction = null,
        [FromQuery(Name = "target_type")] string? targetType = null,
        [FromQuery(Name = "target_id")] string? targetId = null,
        [FromQuery(Name = "tenant_id")] Guid? tenantId = null,
        [FromQuery] string? outcome = null,
        [FromQuery] DateTimeOffset? from = null,
        [FromQuery] DateTimeOffset? to = null,
        CancellationToken ct = default)
    {
        var result = await _mediator.Send(
            new ListPlatformAuditLogsQuery(page, pageSize, actorId, auditAction, targetType, targetId, tenantId, outcome, from, to),
            ct);

        return result.IsSuccess
            ? Ok(result.Value)
            : Problem(result.Error, statusCode: result.StatusCode ?? 400);
    }
}
