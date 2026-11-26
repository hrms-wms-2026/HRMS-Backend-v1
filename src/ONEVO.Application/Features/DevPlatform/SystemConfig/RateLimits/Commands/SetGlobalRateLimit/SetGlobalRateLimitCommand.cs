using MediatR;
using ONEVO.Application.Common.Auditing;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Features.DevPlatform.SystemConfig.RateLimits.DTOs;
using ONEVO.Application.Features.DevPlatform.SystemConfig.RateLimits.Helpers;
using ONEVO.Application.Features.DevPlatform.SystemConfig.RateLimits.Validation;

namespace ONEVO.Application.Features.DevPlatform.SystemConfig.RateLimits.Commands.SetGlobalRateLimit;

/// <summary>Improvements task 47. Needs platform.system_config.manage.</summary>
public sealed record SetGlobalRateLimitCommand(
    RateLimitName LimitName,
    int PerMinute,
    int? Burst,
    string Reason,
    string? RowVersion,
    string? CorrelationId) : IRequest<Result<IReadOnlyList<GlobalRateLimitDto>>>, ISetRateLimitCommand, IAuditedCommand<Result<IReadOnlyList<GlobalRateLimitDto>>>
{
    // Ids, the limit and the values only; the free-text reason stays in rate_limit_setting_changes.
    public AuditEntry DescribeAudit(Result<IReadOnlyList<GlobalRateLimitDto>>? response)
        => new AuditEntry("rate_limit.set", "rate_limit", LimitName.ToStoredName(), null,
            new { Scope = "global", LimitName = LimitName.ToStoredName(), PerMinute, Burst });
}
