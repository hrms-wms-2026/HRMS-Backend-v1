using MediatR;
using ONEVO.Application.Common.Auditing;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Features.DevPlatform.SystemConfig.RateLimits.DTOs;
using ONEVO.Application.Features.DevPlatform.SystemConfig.RateLimits.Helpers;
using ONEVO.Application.Features.DevPlatform.SystemConfig.RateLimits.Validation;

namespace ONEVO.Application.Features.DevPlatform.SystemConfig.RateLimits.Commands.RemoveTenantRateLimit;

/// <summary>Improvements task 47. Needs platform.system_config.manage.</summary>
public sealed record RemoveTenantRateLimitCommand(
    Guid TenantId,
    RateLimitName LimitName,
    string Reason,
    string? RowVersion,
    string? CorrelationId) : IRequest<Result<TenantRateLimitsDto>>, IRemoveRateLimitCommand, IAuditedCommand<Result<TenantRateLimitsDto>>
{
    // Ids, the limit and the values only; the free-text reason stays in rate_limit_setting_changes.
    public AuditEntry DescribeAudit(Result<TenantRateLimitsDto>? response)
        => AuditEntry.For("rate_limit.removed", "tenant", TenantId, TenantId,
            new { Scope = "tenant", LimitName = LimitName.ToStoredName() });
}
