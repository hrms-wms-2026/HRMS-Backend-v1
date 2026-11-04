using MediatR;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Features.DevPlatform.Audit.DTOs.Responses;

namespace ONEVO.Application.Features.DevPlatform.Audit.Queries.ListPlatformAuditLog;

/// <summary>Read-only, newest first. From is inclusive, To is exclusive.</summary>
public sealed record ListPlatformAuditLogQuery(
    int Page = 1,
    int PageSize = 25,
    Guid? ActorPlatformUserId = null,
    string? Action = null,
    string? TargetType = null,
    string? TargetId = null,
    Guid? TargetTenantId = null,
    DateTimeOffset? From = null,
    DateTimeOffset? To = null) : IRequest<Result<PlatformAuditLogListResponse>>;
